using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class VisitMedicineRow
    {
        public int? MedicineId { get; set; }
        public int? Qty { get; set; }
        public string? AturanPakai { get; set; }
    }

    public class VisitFormViewModel
    {
        [Required(ErrorMessage = "Pilih karyawan")]
        public int? EmployeeId { get; set; }

        [Required(ErrorMessage = "Waktu kunjungan wajib diisi")]
        public DateTime? VisitAt { get; set; }            // waktu lokal site

        [Required(ErrorMessage = "Keluhan wajib diisi")]
        [StringLength(1000)]
        public string? Keluhan { get; set; }

        [StringLength(1000)]
        public string? Pemeriksaan { get; set; }

        public List<int> DiseaseIds { get; set; } = new();
        public List<VisitMedicineRow> Medicines { get; set; } = new();

        [Required(ErrorMessage = "Pilih rekomendasi")]
        public string Rekomendasi { get; set; } = RecommendationTypes.KembaliBekerja;

        // Lama istirahat: tanggal mulai s/d tanggal selesai (tanggal lokal site). Jumlah hari dihitung otomatis.
        public DateTime? IstirahatDari { get; set; }
        public DateTime? IstirahatSampai { get; set; }

        [StringLength(1000)]
        public string? CatatanRekomendasi { get; set; }

        public string? EmployeeLabel { get; set; }

        /// <summary>Kalau diisi, kunjungan ini melanjutkan pemeriksaan awal perawat (draft) dan memfinalkannya.</summary>
        public int? TriageId { get; set; }
    }

    /// <summary>Riwayat pasien yang tampil di form kunjungan klinik setelah nama pasien dipilih.</summary>
    public class PatientHistoryViewModel
    {
        public Employee Employee { get; set; } = null!;
        public List<ClinicVisit> Visits { get; set; } = new();
        public int TotalVisits { get; set; }
        public HealthCheckup? LatestCheckup { get; set; }
        public List<HealthCheckup> OpenFollowUps { get; set; } = new();
        public DrugAlcoholTest? LatestDrugTest { get; set; }
        public bool CanSeeCheckups { get; set; }
        public bool CanSeeDrugTests { get; set; }
        public DateTime Today { get; set; }
    }

    /// <summary>Kunjungan klinik: keluhan, diagnosa (master penyakit), obat (potong stok) & rekomendasi. Permission medical.visit.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalVisit)]
    [Route("Admin/ClinicVisits")]
    public class ClinicVisitsController : Controller
    {
        private const int DefaultRangeDays = 7;
        public const int MaxExportRows = 20000;

        private readonly IClinicVisitRepository _repo;
        private readonly IMedicalMasterRepository _masterRepo;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IAppClock _clock;
        private readonly IDashboardNotifier _dashboardNotifier;
        private readonly IHealthCheckupRepository _checkupRepository;
        private readonly IDrugAlcoholTestRepository _drugTestRepository;
        private readonly IPermissionService _permissionService;
        private readonly IMitraRepository _mitraRepository;
        private readonly IClinicTriageRepository _triageRepo;

        public const int MaxHariIstirahat = 60;

        public ClinicVisitsController(IClinicVisitRepository repo, IMedicalMasterRepository masterRepo,
            IEmployeeRepository employeeRepository, IOrganizationRepository orgRepository, IAppClock clock,
            IDashboardNotifier dashboardNotifier, IHealthCheckupRepository checkupRepository,
            IDrugAlcoholTestRepository drugTestRepository, IPermissionService permissionService, IMitraRepository mitraRepository,
            IClinicTriageRepository triageRepo)
        {
            _triageRepo = triageRepo;
            _mitraRepository = mitraRepository;
            _dashboardNotifier = dashboardNotifier;
            _checkupRepository = checkupRepository;
            _drugTestRepository = drugTestRepository;
            _permissionService = permissionService;
            _repo = repo;
            _masterRepo = masterRepo;
            _employeeRepository = employeeRepository;
            _orgRepository = orgRepository;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var today = _clock.Today;
            ViewBag.DefaultFrom = today.AddDays(-(DefaultRangeDays - 1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = today.ToString("yyyy-MM-dd");
            // Filter department juga memuat nama mitra (snapshot kunjungan karyawan mitra = nama mitra)
            ViewBag.Departments = (await _orgRepository.GetDepartmentsAsync()).Select(d => d.Name)
                .Concat((await _mitraRepository.GetAllAsync()).Select(m => m.Nama))
                .Distinct().OrderBy(n => n).ToList();
            // Antrian dokter: draft Pemeriksaan Awal dari perawat yang menunggu (terlama dulu). Kosong kalau tabelnya belum ada.
            ViewBag.Queue = (await _triageRepo.GetListAsync(new ClinicTriageQuery { Status = TriageStatuses.Draft }))
                .OrderBy(t => t.TriageAt).ToList();
            return View();
        }

        [HttpGet("Data")]
        public async Task<IActionResult> Data(int draw, int start, int length, DateTime? from, DateTime? to, string? department, string? status)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);
            var q = BuildQuery(from, to, department, status, Request.Query["search[value]"], sortColumn, Request.Query["order[0][dir]"]);
            q.Skip = start;
            q.Take = length;
            var page = await _repo.GetPageAsync(q);

            return Json(new
            {
                draw,
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                karyawan = page.FilteredEmployees,
                data = page.Rows.Select(v => new
                {
                    id = v.Id,
                    employeeId = v.EmployeeId,
                    waktu = _clock.ToLocal(v.VisitAt).ToString("dd/MM/yyyy HH:mm"),
                    nip = v.NIP,
                    nama = v.NamaSnapshot,
                    department = v.DepartmentSnapshot,
                    keluhan = v.Keluhan,
                    diagnosa = v.DiagnosaText,
                    rekomendasi = v.RekomendasiLabel,
                    rekomendasiClass = RecommendationTypes.BadgeClass(v.Rekomendasi),
                    isCancelled = v.IsCancelled
                })
            });
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create(int? employeeId, int? triageId)
        {
            var model = new VisitFormViewModel { VisitAt = CheckupsController.TrimToMinute(_clock.LocalNow) };

            // Melanjutkan pemeriksaan awal perawat: pasien, keluhan, dan tanda vital terisi dari draft
            if (triageId.HasValue)
            {
                var triage = await _triageRepo.GetByIdAsync(triageId.Value);
                if (triage is null || !triage.IsDraft)
                {
                    TempData["Error"] = triage is null
                        ? "Pemeriksaan awal tidak ditemukan."
                        : "Pemeriksaan awal ini sudah difinalkan atau dibatalkan.";
                    return RedirectToAction(nameof(Index));
                }
                model.TriageId = triage.Id;
                model.EmployeeId = triage.EmployeeId;
                model.EmployeeLabel = $"{triage.NIP} - {triage.NamaSnapshot}";
                model.Keluhan = triage.Keluhan;
                model.Pemeriksaan = triage.BuildPemeriksaanText(_clock.ToLocal(triage.TriageAt));
                ViewBag.Triage = triage;
                await LoadMastersAsync();
                return View(model);
            }

            if (employeeId.HasValue && await _employeeRepository.GetByIdAsync(employeeId.Value) is { } e)
            {
                model.EmployeeId = e.Id;
                model.EmployeeLabel = $"{e.NIP} - {e.Nama}";
            }
            await LoadMastersAsync();
            return View(model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VisitFormViewModel model)
        {
            var employee = model.EmployeeId.HasValue ? await _employeeRepository.GetByIdAsync(model.EmployeeId.Value) : null;
            if (model.EmployeeId.HasValue && employee is null)
                ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan tidak ditemukan");

            if (!RecommendationTypes.All.Contains(model.Rekomendasi))
                ModelState.AddModelError(nameof(model.Rekomendasi), "Rekomendasi tidak valid");

            // Memfinalkan pemeriksaan awal: draft harus ada, masih Draft, dan pasiennya sama
            ClinicTriage? triage = null;
            if (model.TriageId.HasValue)
            {
                triage = await _triageRepo.GetByIdAsync(model.TriageId.Value);
                if (triage is null)
                    ModelState.AddModelError(string.Empty, "Pemeriksaan awal tidak ditemukan.");
                else if (!triage.IsDraft)
                    ModelState.AddModelError(string.Empty, "Pemeriksaan awal ini sudah difinalkan atau dibatalkan oleh pengguna lain.");
                else if (model.EmployeeId != triage.EmployeeId)
                    ModelState.AddModelError(nameof(model.EmployeeId), "Pasien tidak boleh diganti saat melanjutkan pemeriksaan awal");
            }

            int? hariIstirahat = null;
            if (model.Rekomendasi == RecommendationTypes.Istirahat)
            {
                if (model.IstirahatDari is null || model.IstirahatSampai is null)
                    ModelState.AddModelError(nameof(model.IstirahatDari), "Isi tanggal mulai dan tanggal selesai istirahat");
                else if (model.IstirahatSampai.Value.Date < model.IstirahatDari.Value.Date)
                    ModelState.AddModelError(nameof(model.IstirahatSampai), "Tanggal selesai istirahat tidak boleh sebelum tanggal mulai");
                else
                {
                    hariIstirahat = RecommendationTypes.JumlahHari(model.IstirahatDari.Value, model.IstirahatSampai.Value);
                    if (hariIstirahat > MaxHariIstirahat)
                        ModelState.AddModelError(nameof(model.IstirahatSampai), $"Lama istirahat maksimal {MaxHariIstirahat} hari");
                    if (model.VisitAt.HasValue && model.IstirahatDari.Value.Date < model.VisitAt.Value.Date.AddDays(-7))
                        ModelState.AddModelError(nameof(model.IstirahatDari), "Tanggal mulai istirahat terlalu jauh sebelum tanggal kunjungan");
                }
            }
            else
            {
                model.IstirahatDari = null;
                model.IstirahatSampai = null;
            }

            var diseases = await _masterRepo.GetDiseasesAsync(onlyActive: true);
            model.DiseaseIds = model.DiseaseIds.Where(id => diseases.Any(d => d.Id == id)).Distinct().ToList();
            if (model.DiseaseIds.Count == 0)
                ModelState.AddModelError(nameof(model.DiseaseIds), "Pilih minimal satu jenis penyakit / diagnosa");

            // Baris obat kosong (belum dipilih) diabaikan; yang terisi wajib punya jumlah > 0
            model.Medicines = model.Medicines.Where(m => m.MedicineId.HasValue).ToList();
            if (model.Medicines.Any(m => m.Qty is null or <= 0 or > 1000))
                ModelState.AddModelError(nameof(model.Medicines), "Jumlah obat harus 1-1000");

            if (model.VisitAt.HasValue && model.VisitAt.Value > _clock.LocalNow.AddMinutes(10))
                ModelState.AddModelError(nameof(model.VisitAt), "Waktu kunjungan tidak boleh di masa depan");

            if (ModelState.IsValid)
            {
                var visit = new ClinicVisit
                {
                    EmployeeId = employee!.Id,
                    NamaSnapshot = employee.Nama,
                    // Karyawan mitra: department = nama mitra, section = "Mitra Kerja", posisi = jabatan di mitra
                    DepartmentSnapshot = employee.UnitKerja,
                    SectionSnapshot = employee.BagianKerja,
                    PositionSnapshot = employee.JabatanKerja,
                    VisitAt = _clock.ToUtc(model.VisitAt!.Value),
                    Keluhan = model.Keluhan!.Trim(),
                    Pemeriksaan = string.IsNullOrWhiteSpace(model.Pemeriksaan) ? null : model.Pemeriksaan.Trim(),
                    Rekomendasi = model.Rekomendasi,
                    HariIstirahat = hariIstirahat,
                    IstirahatDari = model.IstirahatDari?.Date,
                    IstirahatSampai = model.IstirahatSampai?.Date,
                    CatatanRekomendasi = string.IsNullOrWhiteSpace(model.CatatanRekomendasi) ? null : model.CatatanRekomendasi.Trim(),
                    ExaminerAdminId = User.AdminId(),
                    ExaminerName = User.AdminName()
                };

                try
                {
                    var id = await _repo.CreateAsync(visit, model.DiseaseIds,
                        model.Medicines.Select(m => new VisitMedicineInput(m.MedicineId!.Value, m.Qty!.Value, m.AturanPakai)),
                        model.TriageId);
                    await _dashboardNotifier.NotifyClinicChangedAsync();
                    TempData["Success"] = model.TriageId.HasValue
                        ? $"Kunjungan {employee.Nama} difinalkan. Pemeriksaan awal perawat ditandai selesai."
                        : $"Kunjungan {employee.Nama} berhasil disimpan.";
                    return RedirectToAction(nameof(Detail), new { id });
                }
                catch (StockException ex)
                {
                    // Stok tidak cukup: tidak ada data yang tersimpan, form ditampilkan lagi
                    ModelState.AddModelError(nameof(model.Medicines), ex.Message);
                }
                catch (TriageStateException ex)
                {
                    // Draft sudah difinalkan/dibatalkan pengguna lain tepat sebelum disimpan: tidak ada data yang tersimpan
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }

            ViewBag.Triage = triage;

            if (employee is not null)
                model.EmployeeLabel = $"{employee.NIP} - {employee.Nama}";
            await LoadMastersAsync();
            return View(model);
        }

        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id)
        {
            var v = await _repo.GetByIdAsync(id);
            if (v is null) return NotFound();
            ViewBag.Triage = await _triageRepo.GetByVisitIdAsync(id);   // pemeriksaan awal perawat (kalau kunjungan ini dilanjutkan dari draft)
            return View(v);
        }

        // GET Admin/ClinicVisits/History?employeeId=5  (partial HTML: riwayat pasien untuk form kunjungan)
        [HttpGet("History")]
        public async Task<IActionResult> History(int employeeId)
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null)
                return NotFound();

            var access = await _permissionService.GetAsync(User);
            var vm = new PatientHistoryViewModel
            {
                Employee = employee,
                Visits = await _repo.GetByEmployeeAsync(employeeId, includeCancelled: false, top: 10),
                TotalVisits = await _repo.CountByEmployeeAsync(employeeId),
                Today = _clock.Today
            };
            if (access.Has(Permissions.MedicalCheckup))
            {
                var checkups = await _checkupRepository.GetByEmployeeAsync(employeeId, top: 50);
                vm.LatestCheckup = checkups.FirstOrDefault();
                vm.OpenFollowUps = checkups.Where(c => c.FollowUpOpen).ToList();
                vm.CanSeeCheckups = true;
            }
            if (access.Has(Permissions.MedicalDrugTest))
            {
                vm.LatestDrugTest = (await _drugTestRepository.GetByEmployeeAsync(employeeId, top: 1)).FirstOrDefault();
                vm.CanSeeDrugTests = true;
            }
            return PartialView("_PatientHistory", vm);
        }

        // Batalkan (bukan hapus): data tetap tersimpan sebagai jejak, stok obat dikembalikan
        [HttpPost("Cancel/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "Alasan pembatalan wajib diisi.";
                return RedirectToAction(nameof(Detail), new { id });
            }

            try
            {
                await _repo.CancelAsync(id, reason.Trim(), User.AdminId(), User.AdminName());
                await _dashboardNotifier.NotifyClinicChangedAsync();
                TempData["Success"] = "Kunjungan dibatalkan dan stok obat sudah dikembalikan.";
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department, string? status, string? search, int sort = 0, string? dir = "desc")
        {
            var rows = await _repo.GetForExportAsync(BuildQuery(from, to, department, status, search, sort, dir), MaxExportRows);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Kunjungan Klinik");
            string[] headers =
            {
                "No", "Tanggal", "Jam", "NIP", "Nama", "Department", "Section", "Keluhan", "Pemeriksaan", "Diagnosa",
                "Obat", "Rekomendasi", "Hari Istirahat", "Istirahat Dari", "Istirahat Sampai", "Catatan Rekomendasi", "Pemeriksa", "Status"
            };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            var head = ws.Range(1, 1, 1, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");

            var r = 2;
            foreach (var v in rows)
            {
                var local = _clock.ToLocal(v.VisitAt);
                ws.Cell(r, 1).Value = r - 1;
                ws.Cell(r, 2).Value = local.Date;
                ws.Cell(r, 3).Value = local.ToString("HH:mm");
                ws.Cell(r, 4).Value = v.NIP ?? "";
                ws.Cell(r, 5).Value = v.NamaSnapshot;
                ws.Cell(r, 6).Value = v.DepartmentSnapshot;
                ws.Cell(r, 7).Value = v.SectionSnapshot;
                ws.Cell(r, 8).Value = v.Keluhan;
                ws.Cell(r, 9).Value = v.Pemeriksaan ?? "";
                ws.Cell(r, 10).Value = v.DiagnosaText ?? "";
                ws.Cell(r, 11).Value = string.Join("; ", v.Medicines.Select(m =>
                    $"{m.NamaSnapshot} {m.Qty} {m.SatuanSnapshot}{(string.IsNullOrEmpty(m.AturanPakai) ? "" : " (" + m.AturanPakai + ")")}"));
                ws.Cell(r, 12).Value = RecommendationTypes.Label(v.Rekomendasi);
                if (v.HariIstirahat.HasValue) ws.Cell(r, 13).Value = v.HariIstirahat.Value;
                if (v.IstirahatDari.HasValue) ws.Cell(r, 14).Value = v.IstirahatDari.Value;
                if (v.IstirahatSampai.HasValue) ws.Cell(r, 15).Value = v.IstirahatSampai.Value;
                ws.Cell(r, 16).Value = v.CatatanRekomendasi ?? "";
                ws.Cell(r, 17).Value = v.ExaminerName;
                ws.Cell(r, 18).Value = v.IsCancelled ? $"DIBATALKAN: {v.CancelReason}" : "Aktif";
                if (v.IsCancelled)
                    ws.Range(r, 1, r, headers.Length).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
                r++;
            }

            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(14).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(15).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(4).Style.NumberFormat.Format = "@";
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            foreach (var c in new[] { 8, 9, 10, 11, 16 }) ws.Column(c).Width = Math.Min(ws.Column(c).Width, 50);
            ws.Range(1, 1, Math.Max(r - 1, 1), headers.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Kunjungan_Klinik_{_clock.Today:yyyyMMdd}.xlsx");
        }

        private async Task LoadMastersAsync()
        {
            ViewBag.Diseases = await _masterRepo.GetDiseasesAsync(onlyActive: true);
            ViewBag.MedicineList = await _masterRepo.GetMedicinesAsync(onlyActive: true);
        }

        private MedicalPageQuery BuildQuery(DateTime? from, DateTime? to, string? department, string? status, string? search, int sort, string? dir) => new()
        {
            Search = search,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Department = department,
            Status = status == "batal" || RecommendationTypes.All.Contains(status) ? status : null,
            SortColumn = sort,
            SortDesc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase)
        };
    }
}
