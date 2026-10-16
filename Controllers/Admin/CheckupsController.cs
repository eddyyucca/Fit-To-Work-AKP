using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers.Admin
{
    public class CheckupFormViewModel
    {
        [Required(ErrorMessage = "Pilih karyawan")]
        public int? EmployeeId { get; set; }

        [Required(ErrorMessage = "Waktu pemeriksaan wajib diisi")]
        public DateTime? ExaminedAt { get; set; }       // waktu lokal site (WITA)

        // Nilai parameter pemkes (menu Parameter & Skor Pemkes). Key = Id parameter;
        // angka untuk tipe Number, Id kriteria untuk tipe Choice. Umur & jenis kelamin otomatis dari data karyawan.
        public Dictionary<int, string?> Values { get; set; } = new();

        // Untuk menghitung IMT (parameter tipe IMT)
        [Range(20, 300, ErrorMessage = "Berat 20-300 kg")] public decimal? BeratBadan { get; set; }
        [Range(100, 230, ErrorMessage = "Tinggi 100-230 cm")] public decimal? TinggiBadan { get; set; }

        [StringLength(1000)] public string? Keluhan { get; set; }
        [StringLength(1000)] public string? Catatan { get; set; }

        // Temuan & follow up
        public bool AdaTemuan { get; set; }
        [StringLength(1000)] public string? TemuanKeterangan { get; set; }
        public DateTime? FollowUpDate { get; set; }     // tanggal lokal site

        // Lampiran hasil pemeriksaan (PDF/JPG/PNG)
        public List<IFormFile>? Files { get; set; }

        // Hanya untuk menampilkan ulang karyawan terpilih saat validasi gagal
        public string? EmployeeLabel { get; set; }
        public int? EmployeeUmur { get; set; }
        public string? EmployeeJenisKelamin { get; set; }
    }

    public class FollowUpFormViewModel
    {
        public bool AdaTemuan { get; set; } = true;
        [StringLength(1000)] public string? TemuanKeterangan { get; set; }
        public DateTime? FollowUpDate { get; set; }
        public string? ReturnTo { get; set; }           // "list" = kembali ke daftar follow up
    }

    /// <summary>Pemeriksaan kesehatan berkala (diinput medic). Butuh permission medical.checkup.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalCheckup)]
    [Route("Admin/Checkups")]
    public partial class CheckupsController : Controller
    {
        private const int DefaultRangeDays = 30;
        public const int MaxExportRows = 20000;

        private readonly IHealthCheckupRepository _repo;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IMedicalAttachmentRepository _attachmentRepository;
        private readonly ICheckupParameterRepository _parameterRepository;
        private readonly IAppClock _clock;
        private readonly IMemoryCache _cache;   // pratinjau import Excel (CheckupsController.Import.cs)

        public CheckupsController(IHealthCheckupRepository repo, IEmployeeRepository employeeRepository,
            IOrganizationRepository orgRepository, IMedicalAttachmentRepository attachmentRepository,
            ICheckupParameterRepository parameterRepository, IAppClock clock, IMemoryCache cache)
        {
            _cache = cache;
            _parameterRepository = parameterRepository;
            _repo = repo;
            _employeeRepository = employeeRepository;
            _orgRepository = orgRepository;
            _attachmentRepository = attachmentRepository;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var today = _clock.Today;
            ViewBag.DefaultFrom = today.AddDays(-(DefaultRangeDays - 1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = today.ToString("yyyy-MM-dd");
            ViewBag.Departments = (await _orgRepository.GetDepartmentsAsync()).Select(d => d.Name).Distinct().OrderBy(n => n).ToList();
            return View();
        }

        [HttpGet("Data")]
        public async Task<IActionResult> Data(int draw, int start, int length, DateTime? from, DateTime? to, string? department)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);
            var q = BuildQuery(from, to, department, Request.Query["search[value]"], sortColumn, Request.Query["order[0][dir]"]);
            q.Skip = start;
            q.Take = length;
            var page = await _repo.GetPageAsync(q);

            return Json(new
            {
                draw,
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                karyawan = page.FilteredEmployees,
                data = page.Rows.Select(ToRow)
            });
        }

        private object ToRow(HealthCheckup c)
        {
            var abnormal = CheckupDisplay.Abnormal(c).ToList();
            return new
            {
                id = c.Id,
                employeeId = c.EmployeeId,
                waktu = _clock.ToLocal(c.ExaminedAt).ToString("dd/MM/yyyy HH:mm"),
                nip = c.NIP,
                nama = c.NamaSnapshot,
                department = c.DepartmentSnapshot,
                skor = c.TotalSkor,
                skorLengkap = c.SkorLengkap,
                skorInfo = c.UsesParameters && c.SkorParamTotal.HasValue ? $"{c.SkorParamTerisi}/{c.SkorParamTotal} parameter berpoin terisi" : null,
                risiko = c.RisikoLabel,
                risikoClass = RiskColors.BadgeClass(c.RisikoColor),
                // Nilai di luar normal, mis. "Kolesterol Total 245 mg/dL (240 – 279)"
                temuan = abnormal.Select(r => new { label = r.Label, nilai = r.ValueText, kriteria = r.CriteriaLabel, cls = CheckupLevels.BadgeClass(r.Level) }),
                keluhan = c.Keluhan,
                followUp = c.AdaTemuan ? new
                {
                    status = c.FollowUpStatus(_clock.Today),
                    label = FollowUpStatuses.Label(c.FollowUpStatus(_clock.Today)),
                    badge = FollowUpStatuses.BadgeClass(c.FollowUpStatus(_clock.Today)),
                    tanggal = c.FollowUpDate?.ToString("dd/MM/yyyy")
                } : null,
                lampiran = c.AttachmentCount
            };
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create(int? employeeId)
        {
            var model = new CheckupFormViewModel { ExaminedAt = TrimToMinute(_clock.LocalNow) };
            if (employeeId.HasValue && await _employeeRepository.GetByIdAsync(employeeId.Value) is { } e)
                SetEmployeeInfo(model, e);
            await LoadFormAsync();
            return View(model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(110L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024 * 1024)]
        public async Task<IActionResult> Create(CheckupFormViewModel model)
        {
            var employee = model.EmployeeId.HasValue ? await _employeeRepository.GetByIdAsync(model.EmployeeId.Value) : null;
            if (model.EmployeeId.HasValue && employee is null)
                ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan tidak ditemukan");
            else if (employee is { IsMitra: true })
                ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan mitra hanya untuk kunjungan klinik, bukan pemeriksaan kesehatan.");

            if (model.ExaminedAt.HasValue && model.ExaminedAt.Value > _clock.LocalNow.AddMinutes(10))
                ModelState.AddModelError(nameof(model.ExaminedAt), "Waktu pemeriksaan tidak boleh di masa depan");

            if (model.BeratBadan.HasValue != model.TinggiBadan.HasValue)
                ModelState.AddModelError(nameof(model.TinggiBadan), "Isi berat dan tinggi badan sekaligus untuk menghitung IMT");

            // Nilai parameter dinilai di server (poin, kriteria, warna, total skor & kategori risiko)
            var parameters = await _parameterRepository.GetAllAsync(onlyActive: true);
            var riskLevels = await _parameterRepository.GetRiskLevelsAsync();
            var score = CheckupScoring.Evaluate(parameters, riskLevels, new CheckupScoreInput
            {
                Values = model.Values,
                BeratBadan = Round1(model.BeratBadan),
                TinggiBadan = Round1(model.TinggiBadan),
                TanggalLahir = employee?.TanggalLahir,
                JenisKelamin = employee?.JenisKelamin,
                TanggalPemeriksaan = (model.ExaminedAt ?? _clock.LocalNow).Date
            });
            foreach (var err in score.Errors)
                ModelState.AddModelError(string.Empty, err);

            // Umur & jenis kelamin terisi otomatis, jadi tidak dihitung sebagai "isian" medic
            var adaIsian = score.Values.Any(v => v.InputType is not (CheckupInputTypes.Age or CheckupInputTypes.Sex))
                || !string.IsNullOrWhiteSpace(model.Keluhan);
            if (!adaIsian)
                ModelState.AddModelError(string.Empty, "Isi minimal satu hasil pemeriksaan atau keluhan.");

            ValidateFollowUp(ModelState, model.AdaTemuan, model.TemuanKeterangan, model.FollowUpDate, model.ExaminedAt?.Date);

            foreach (var err in _attachmentRepository.Validate(model.Files))
                ModelState.AddModelError(nameof(model.Files), err);

            if (!ModelState.IsValid)
            {
                if (employee is not null)
                    SetEmployeeInfo(model, employee);
                if (model.Files?.Any(f => f.Length > 0) == true)
                    ModelState.AddModelError(nameof(model.Files), "File lampiran perlu dipilih ulang.");
                await LoadFormAsync(parameters, riskLevels);
                return View(model);
            }

            var id = await _repo.CreateAsync(new HealthCheckup
            {
                EmployeeId = employee!.Id,
                NamaSnapshot = employee.Nama,
                DepartmentSnapshot = employee.DepartmentName ?? string.Empty,
                SectionSnapshot = employee.SectionName ?? string.Empty,
                PositionSnapshot = employee.PositionName ?? string.Empty,
                JenisKelamin = employee.JenisKelamin,
                ExaminedAt = _clock.ToUtc(model.ExaminedAt!.Value),
                BeratBadan = Round1(model.BeratBadan),
                TinggiBadan = Round1(model.TinggiBadan),
                Keluhan = Clean(model.Keluhan),
                Catatan = Clean(model.Catatan),
                AdaTemuan = model.AdaTemuan,
                TemuanKeterangan = model.AdaTemuan ? Clean(model.TemuanKeterangan) : null,
                FollowUpDate = model.AdaTemuan ? model.FollowUpDate?.Date : null,
                UsesParameters = true,
                Values = score.Values,
                TotalSkor = score.ScoredFilled > 0 ? score.Total : null,
                SkorParamTerisi = score.ScoredFilled,
                SkorParamTotal = score.ScoredTotal,
                RisikoLabel = score.Risk?.Label,
                RisikoColor = score.Risk?.Color,
                ExaminerAdminId = User.AdminId(),
                ExaminerName = User.AdminName()
            });

            var files = await _attachmentRepository.SaveAsync(AttachmentEntityTypes.Checkup, id, model.Files, User.AdminName());

            TempData["Success"] = $"Hasil pemeriksaan {employee.Nama} berhasil disimpan"
                + (score.Risk is not null ? $" (skor {score.Total}, {score.Risk.Label})" : "")
                + (files > 0 ? $" beserta {files} lampiran." : ".")
                + (model.AdaTemuan ? $" Follow up dijadwalkan {model.FollowUpDate:dd/MM/yyyy}." : "");
            return RedirectToAction(nameof(Detail), new { id });
        }

        private void SetEmployeeInfo(CheckupFormViewModel model, Employee e)
        {
            model.EmployeeId = e.Id;
            model.EmployeeLabel = $"{e.NIP} - {e.Nama}";
            model.EmployeeJenisKelamin = e.JenisKelamin;
            model.EmployeeUmur = e.TanggalLahir.HasValue ? CheckupScoring.Age(e.TanggalLahir.Value, _clock.Today) : null;
        }

        private async Task LoadFormAsync(List<CheckupParameter>? parameters = null, List<CheckupRiskLevel>? riskLevels = null)
        {
            ViewBag.Parameters = parameters ?? await _parameterRepository.GetAllAsync(onlyActive: true);
            ViewBag.RiskLevels = riskLevels ?? await _parameterRepository.GetRiskLevelsAsync();
        }

        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c is null)
                return NotFound();
            ViewBag.Attachments = new AttachmentListViewModel
            {
                EntityType = AttachmentEntityTypes.Checkup,
                EntityId = c.Id,
                Items = await _attachmentRepository.GetForEntityAsync(AttachmentEntityTypes.Checkup, c.Id),
                CanEdit = true
            };
            ViewBag.Today = _clock.Today;
            ViewBag.SuggestedFollowUp = _clock.ToLocal(c.ExaminedAt).Date.AddDays(HealthRanges.FollowUpDefaultHari);
            return View(c);
        }

        // ======================= Follow up =======================

        // GET Admin/Checkups/FollowUps?status=open|overdue|today|done|all
        [HttpGet("FollowUps")]
        public async Task<IActionResult> FollowUps(string? status)
        {
            status = status is "overdue" or "today" or "done" or "all" ? status : "open";
            var today = _clock.Today;
            ViewBag.Status = status;
            ViewBag.Today = today;
            ViewBag.Counts = await _repo.CountFollowUpsAsync(today);
            return View(await _repo.GetFollowUpsAsync(status, today));
        }

        // Tambah / ubah temuan & jadwal follow up dari halaman detail
        [HttpPost("FollowUp/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FollowUp(int id, FollowUpFormViewModel model)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c is null)
                return NotFound();

            ValidateFollowUp(ModelState, model.AdaTemuan, model.TemuanKeterangan, model.FollowUpDate, _clock.ToLocal(c.ExaminedAt).Date);
            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Detail), new { id });
            }

            await _repo.UpdateFollowUpAsync(id, model.AdaTemuan, Clean(model.TemuanKeterangan), model.FollowUpDate);
            TempData["Success"] = model.AdaTemuan
                ? $"Temuan disimpan, follow up {c.NamaSnapshot} dijadwalkan {model.FollowUpDate:dd/MM/yyyy}."
                : "Temuan & jadwal follow up dihapus.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpPost("FollowUpDone/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FollowUpDone(int id, string? hasil, string? returnTo)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c is null)
                return NotFound();

            if (!c.AdaTemuan)
                TempData["Error"] = "Pemeriksaan ini tidak punya temuan / jadwal follow up.";
            else if (string.IsNullOrWhiteSpace(hasil))
                TempData["Error"] = "Isi hasil / tindakan follow up.";
            else
            {
                await _repo.CompleteFollowUpAsync(id, Truncate(hasil.Trim(), 1000), User.AdminName());
                TempData["Success"] = $"Follow up {c.NamaSnapshot} ditandai selesai.";
            }

            return returnTo == "list" ? RedirectToAction(nameof(FollowUps)) : RedirectToAction(nameof(Detail), new { id });
        }

        [HttpPost("FollowUpReopen/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FollowUpReopen(int id)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c is null)
                return NotFound();
            await _repo.ReopenFollowUpAsync(id);
            TempData["Success"] = "Follow up dibuka kembali (status belum selesai).";
            return RedirectToAction(nameof(Detail), new { id });
        }

        private static void ValidateFollowUp(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ms,
            bool adaTemuan, string? keterangan, DateTime? followUpDate, DateTime? examinedDate)
        {
            if (!adaTemuan)
                return;
            if (string.IsNullOrWhiteSpace(keterangan))
                ms.AddModelError(nameof(CheckupFormViewModel.TemuanKeterangan), "Isi keterangan temuan / rencana follow up");
            if (followUpDate is null)
                ms.AddModelError(nameof(CheckupFormViewModel.FollowUpDate), "Isi tanggal follow up");
            else if (examinedDate.HasValue && followUpDate.Value.Date < examinedDate.Value)
                ms.AddModelError(nameof(CheckupFormViewModel.FollowUpDate), "Tanggal follow up tidak boleh sebelum tanggal pemeriksaan");
            else if (examinedDate.HasValue && followUpDate.Value.Date > examinedDate.Value.AddYears(2))
                ms.AddModelError(nameof(CheckupFormViewModel.FollowUpDate), "Tanggal follow up maksimal 2 tahun setelah pemeriksaan");
        }

        // Hapus hanya untuk koreksi salah input (tercatat di Log Akses)
        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c is null)
                return NotFound();
            await _repo.DeleteAsync(id);
            await _attachmentRepository.DeleteForEntityAsync(AttachmentEntityTypes.Checkup, id);
            TempData["Success"] = $"Pemeriksaan {c.NamaSnapshot} tanggal {_clock.ToLocal(c.ExaminedAt):dd/MM/yyyy HH:mm} dihapus.";
            return RedirectToAction("Employee", "MedicalRecord", new { id = c.EmployeeId });
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department, string? search, int sort = 0, string? dir = "desc")
        {
            var rows = await _repo.GetForExportAsync(BuildQuery(from, to, department, search, sort, dir), MaxExportRows);
            var display = rows.ToDictionary(c => c.Id, CheckupDisplay.Rows);

            // Kolom parameter mengikuti semua parameter yang muncul di data (urutan kemunculan pertama)
            var paramCols = display.Values.SelectMany(list => list.Select(x => x.Label)).Distinct().ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Pemeriksaan");
            var headers = new List<string> { "No", "Tanggal", "Jam", "NIP", "Nama", "Department", "Section", "JK" };
            headers.AddRange(paramCols);
            headers.AddRange(new[] { "Total Skor", "Kategori Risiko", "Nilai di Luar Normal", "Keluhan", "Catatan", "Pemeriksa",
                "Temuan / Rencana Follow Up", "Tgl Follow Up", "Status Follow Up", "Hasil Follow Up" });
            for (int i = 0; i < headers.Count; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            var head = ws.Range(1, 1, 1, headers.Count);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");

            XLColor? Fill(string level) => level switch
            {
                CheckupLevels.Risiko => XLColor.FromHtml("#F8D7DA"),
                CheckupLevels.Perhatian => XLColor.FromHtml("#FFF3CD"),
                CheckupLevels.Info => XLColor.FromHtml("#CFF4FC"),
                _ => null
            };

            var firstParamCol = 9;
            var afterParamCol = firstParamCol + paramCols.Count;
            var r = 2;
            foreach (var c in rows)
            {
                var local = _clock.ToLocal(c.ExaminedAt);
                ws.Cell(r, 1).Value = r - 1;
                ws.Cell(r, 2).Value = local.Date;
                ws.Cell(r, 3).Value = local.ToString("HH:mm");
                ws.Cell(r, 4).Value = c.NIP ?? "";
                ws.Cell(r, 5).Value = c.NamaSnapshot;
                ws.Cell(r, 6).Value = c.DepartmentSnapshot;
                ws.Cell(r, 7).Value = c.SectionSnapshot;
                ws.Cell(r, 8).Value = c.JenisKelamin ?? "";

                var list = display[c.Id];
                foreach (var d in list)
                {
                    var cell = ws.Cell(r, firstParamCol + paramCols.IndexOf(d.Label));
                    if (d.Number.HasValue) cell.Value = d.Number.Value; else cell.Value = d.ValueText;
                    if (Fill(d.Level) is { } color) cell.Style.Fill.BackgroundColor = color;
                    if (!string.IsNullOrEmpty(d.CriteriaLabel))
                        cell.CreateComment().AddText(d.CriteriaLabel + (d.Points.HasValue ? $" = {d.Points} poin" : ""));
                }

                var col = afterParamCol;
                if (c.TotalSkor.HasValue) ws.Cell(r, col).Value = c.TotalSkor.Value;
                col++;
                ws.Cell(r, col++).Value = c.RisikoLabel ?? "";
                ws.Cell(r, col++).Value = string.Join("; ", list.Where(x => x.IsAbnormal).Select(x => $"{x.Label} {x.ValueText}"));
                ws.Cell(r, col++).Value = c.Keluhan ?? "";
                ws.Cell(r, col++).Value = c.Catatan ?? "";
                ws.Cell(r, col++).Value = c.ExaminerName;
                ws.Cell(r, col++).Value = c.AdaTemuan ? c.TemuanKeterangan ?? "" : "";
                if (c.AdaTemuan && c.FollowUpDate.HasValue) ws.Cell(r, col).Value = c.FollowUpDate.Value;
                col++;
                ws.Cell(r, col++).Value = c.AdaTemuan ? FollowUpStatuses.Label(c.FollowUpStatus(_clock.Today)) : "";
                ws.Cell(r, col++).Value = c.FollowUpHasil ?? "";
                r++;
            }

            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(afterParamCol + 7).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(4).Style.NumberFormat.Format = "@";
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            foreach (var wide in new[] { 2, 3, 4, 6, 9 })
                ws.Column(afterParamCol + wide).Width = Math.Min(ws.Column(afterParamCol + wide).Width, 45);
            ws.Range(1, 1, Math.Max(r - 1, 1), headers.Count).SetAutoFilter();
            ws.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Pemeriksaan_Kesehatan_{_clock.Today:yyyyMMdd}.xlsx");
        }

        private MedicalPageQuery BuildQuery(DateTime? from, DateTime? to, string? department, string? search, int sort, string? dir) => new()
        {
            Search = search,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Department = department,
            SortColumn = sort,
            SortDesc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase)
        };

        private static decimal? Round1(decimal? v) => v.HasValue ? Math.Round(v.Value, 1) : null;
        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
        internal static DateTime TrimToMinute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
    }
}
