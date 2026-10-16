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
    public class UrineItemRow
    {
        public string? ParameterName { get; set; }
        public string? Hasil { get; set; }
    }

    public class DrugTestFormViewModel
    {
        [Required(ErrorMessage = "Pilih karyawan")]
        public int? EmployeeId { get; set; }

        [Required(ErrorMessage = "Waktu tes wajib diisi")]
        public DateTime? TestedAt { get; set; }          // waktu lokal site

        public string Alasan { get; set; } = DrugTestReasons.Rutin;

        public bool TesUrin { get; set; } = true;
        public List<UrineItemRow> UrineItems { get; set; } = new();

        public bool TesAlkohol { get; set; } = true;

        [Range(0, 99, ErrorMessage = "Kadar alkohol 0 - 99")]
        public decimal? AlkoholKadar { get; set; }
        public string? AlkoholHasil { get; set; }        // dipakai kalau kadar tidak diisi (alat hanya menampilkan lulus/gagal)

        [StringLength(1000)]
        public string? Catatan { get; set; }

        public List<IFormFile>? Files { get; set; }

        public string? EmployeeLabel { get; set; }
    }

    /// <summary>Tes Urin (narkoba) & Tes Alkohol untuk karyawan AKP dan karyawan mitra. Permission medical.drugtest.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalDrugTest)]
    [Route("Admin/DrugTests")]
    public class DrugTestsController : Controller
    {
        private const int DefaultRangeDays = 30;
        private const int MaxRows = 5000;

        private readonly IDrugAlcoholTestRepository _repo;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IMedicalLookupRepository _lookupRepository;
        private readonly IMedicalAttachmentRepository _attachmentRepository;
        private readonly IAppClock _clock;
        private readonly IMitraRepository _mitraRepository;

        public DrugTestsController(IDrugAlcoholTestRepository repo, IEmployeeRepository employeeRepository,
            IOrganizationRepository orgRepository, IMedicalLookupRepository lookupRepository,
            IMedicalAttachmentRepository attachmentRepository, IAppClock clock, IMitraRepository mitraRepository)
        {
            _mitraRepository = mitraRepository;
            _repo = repo;
            _employeeRepository = employeeRepository;
            _orgRepository = orgRepository;
            _lookupRepository = lookupRepository;
            _attachmentRepository = attachmentRepository;
            _clock = clock;
        }

        // GET Admin/DrugTests?from=..&to=..&department=..&hasil=..&jenis=..&kelompok=akp|mitra
        [HttpGet("")]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department, string? hasil, string? jenis, string? kelompok)
        {
            var today = _clock.Today;
            from ??= today.AddDays(-(DefaultRangeDays - 1));
            to ??= today;
            if (to < from) (from, to) = (to, from);

            ViewBag.From = from.Value.ToString("yyyy-MM-dd");
            ViewBag.To = to.Value.ToString("yyyy-MM-dd");
            ViewBag.Department = department;
            ViewBag.Hasil = hasil;
            ViewBag.Jenis = jenis;
            ViewBag.Kelompok = kelompok;
            ViewBag.Departments = (await _orgRepository.GetDepartmentsAsync()).Select(d => d.Name).Distinct().OrderBy(n => n).ToList();
            // Untuk karyawan mitra, kolom Department berisi nama mitra
            ViewBag.Mitras = (await _mitraRepository.GetAllAsync()).Select(m => m.Nama).Distinct().OrderBy(n => n).ToList();
            ViewBag.MaxRows = MaxRows;

            var rows = await _repo.GetListAsync(BuildQuery(from, to, department, hasil, jenis, kelompok), MaxRows);
            return View(rows);
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create(int? employeeId)
        {
            var model = new DrugTestFormViewModel { TestedAt = CheckupsController.TrimToMinute(_clock.LocalNow) };
            if (employeeId.HasValue && await _employeeRepository.GetByIdAsync(employeeId.Value) is { } e)
            {
                model.EmployeeId = e.Id;
                model.EmployeeLabel = $"{e.NIP} - {e.Nama}";
            }
            var parameters = await _lookupRepository.GetNamesAsync(MedicalLookupKind.UrineParameter);
            model.UrineItems = parameters.Select(p => new UrineItemRow { ParameterName = p, Hasil = TestResults.Negatif }).ToList();
            ViewBag.Parameters = parameters;
            return View(model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(110L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024 * 1024)]
        public async Task<IActionResult> Create(DrugTestFormViewModel model)
        {
            var employee = model.EmployeeId.HasValue ? await _employeeRepository.GetByIdAsync(model.EmployeeId.Value) : null;
            if (model.EmployeeId.HasValue && employee is null)
                ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan tidak ditemukan");

            if (!DrugTestReasons.All.Contains(model.Alasan))
                ModelState.AddModelError(nameof(model.Alasan), "Pilih alasan tes");

            if (!model.TesUrin && !model.TesAlkohol)
                ModelState.AddModelError(string.Empty, "Pilih minimal satu jenis tes (urin / alkohol).");

            if (model.TestedAt.HasValue && model.TestedAt.Value > _clock.LocalNow.AddMinutes(10))
                ModelState.AddModelError(nameof(model.TestedAt), "Waktu tes tidak boleh di masa depan");

            // ---------- Tes urin: hanya parameter yang masih aktif di master ----------
            var parameters = await _lookupRepository.GetNamesAsync(MedicalLookupKind.UrineParameter);
            var urineItems = new List<DrugTestUrineItem>();
            if (model.TesUrin)
            {
                foreach (var row in model.UrineItems.Where(r => !string.IsNullOrWhiteSpace(r.ParameterName)))
                {
                    var name = row.ParameterName!.Trim();
                    if (!parameters.Contains(name) || urineItems.Any(i => i.ParameterName == name))
                        continue;
                    urineItems.Add(new DrugTestUrineItem
                    {
                        ParameterName = name,
                        Hasil = row.Hasil == TestResults.Positif ? TestResults.Positif : TestResults.Negatif
                    });
                }
                if (urineItems.Count == 0)
                    ModelState.AddModelError(nameof(model.UrineItems), "Tidak ada parameter tes urin yang diisi. Cek master Parameter Tes Urin.");
            }

            // ---------- Tes alkohol: kadar diisi -> hasil dihitung otomatis dari batas di Acuan Nilai ----------
            string? alkoholHasil = null;
            if (model.TesAlkohol)
            {
                if (model.AlkoholKadar.HasValue)
                    alkoholHasil = HealthRanges.AlkoholHasil(model.AlkoholKadar.Value);
                else if (TestResults.All.Contains(model.AlkoholHasil))
                    alkoholHasil = model.AlkoholHasil;
                else
                    ModelState.AddModelError(nameof(model.AlkoholKadar), "Isi kadar alkohol dari alat, atau pilih hasil Negatif/Positif");
            }

            foreach (var err in _attachmentRepository.Validate(model.Files))
                ModelState.AddModelError(nameof(model.Files), err);

            if (!ModelState.IsValid)
            {
                if (employee is not null)
                    model.EmployeeLabel = $"{employee.NIP} - {employee.Nama}";
                if (model.Files?.Any(f => f.Length > 0) == true)
                    ModelState.AddModelError(nameof(model.Files), "File lampiran perlu dipilih ulang.");
                // Tampilkan ulang semua parameter aktif dengan pilihan yang sudah diisi
                model.UrineItems = parameters.Select(p => new UrineItemRow
                {
                    ParameterName = p,
                    Hasil = model.UrineItems.FirstOrDefault(r => r.ParameterName == p)?.Hasil ?? TestResults.Negatif
                }).ToList();
                ViewBag.Parameters = parameters;
                return View(model);
            }

            var test = new DrugAlcoholTest
            {
                EmployeeId = employee!.Id,
                NamaSnapshot = employee.Nama,
                // Karyawan mitra: Department = nama mitra, Section = "Mitra Kerja", Jabatan = jabatan di mitra
                DepartmentSnapshot = employee.UnitKerja,
                SectionSnapshot = employee.BagianKerja,
                PositionSnapshot = employee.JabatanKerja,
                TestedAt = _clock.ToUtc(model.TestedAt!.Value),
                Alasan = model.Alasan,
                TesUrin = model.TesUrin,
                UrinHasil = model.TesUrin ? (urineItems.Any(i => i.Hasil == TestResults.Positif) ? TestResults.Positif : TestResults.Negatif) : null,
                TesAlkohol = model.TesAlkohol,
                AlkoholKadar = model.TesAlkohol ? model.AlkoholKadar : null,
                AlkoholBatas = model.TesAlkohol && model.AlkoholKadar.HasValue ? HealthRanges.AlkoholBatas : null,
                AlkoholHasil = alkoholHasil,
                Catatan = string.IsNullOrWhiteSpace(model.Catatan) ? null : model.Catatan.Trim(),
                ExaminerAdminId = User.AdminId(),
                ExaminerName = User.AdminName()
            };

            var id = await _repo.CreateAsync(test, urineItems);
            var files = await _attachmentRepository.SaveAsync(AttachmentEntityTypes.DrugTest, id, model.Files, User.AdminName());

            TempData[test.IsPositive ? "Error" : "Success"] = test.IsPositive
                ? $"Hasil tes {employee.Nama} tersimpan: POSITIF. Tindak lanjuti sesuai prosedur."
                : $"Hasil tes {employee.Nama} tersimpan: negatif" + (files > 0 ? $", {files} lampiran." : ".");
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null)
                return NotFound();
            ViewBag.Attachments = new AttachmentListViewModel
            {
                EntityType = AttachmentEntityTypes.DrugTest,
                EntityId = t.Id,
                Items = await _attachmentRepository.GetForEntityAsync(AttachmentEntityTypes.DrugTest, t.Id),
                CanEdit = true
            };
            return View(t);
        }

        // Hapus hanya untuk koreksi salah input (tercatat di Log Akses)
        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null)
                return NotFound();
            await _repo.DeleteAsync(id);
            await _attachmentRepository.DeleteForEntityAsync(AttachmentEntityTypes.DrugTest, id);
            TempData["Success"] = $"Tes {t.NamaSnapshot} tanggal {_clock.ToLocal(t.TestedAt):dd/MM/yyyy HH:mm} dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department, string? hasil, string? jenis, string? kelompok)
        {
            var rows = await _repo.GetListAsync(BuildQuery(from, to, department, hasil, jenis, kelompok), 20000, withItems: true);

            // Kolom parameter urin mengikuti semua nama yang muncul di data (urutan kemunculan)
            var paramNames = rows.SelectMany(r => r.UrineItems.Select(i => i.ParameterName)).Distinct().ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Tes Urin & Alkohol");
            var headers = new List<string> { "No", "Tanggal", "Jam", "NIP / ID", "Nama", "Karyawan", "Department / Mitra", "Section", "Jabatan", "Alasan", "Hasil Urin" };
            headers.AddRange(paramNames);
            headers.AddRange(new[] { "Kadar Alkohol", "Batas", "Hasil Alkohol", "Hasil Akhir", "Catatan", "Lampiran", "Pemeriksa" });
            for (int i = 0; i < headers.Count; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            var head = ws.Range(1, 1, 1, headers.Count);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");

            var bad = XLColor.FromHtml("#F8D7DA");
            var r = 2;
            foreach (var t in rows)
            {
                var local = _clock.ToLocal(t.TestedAt);
                var c = 1;
                ws.Cell(r, c++).Value = r - 1;
                ws.Cell(r, c++).Value = local.Date;
                ws.Cell(r, c++).Value = local.ToString("HH:mm");
                ws.Cell(r, c++).Value = t.NIP ?? "";
                ws.Cell(r, c++).Value = t.NamaSnapshot;
                ws.Cell(r, c++).Value = t.IsMitra ? "Mitra" : "AKP";
                ws.Cell(r, c++).Value = t.DepartmentSnapshot;
                ws.Cell(r, c++).Value = t.SectionSnapshot;
                ws.Cell(r, c++).Value = t.PositionSnapshot;
                ws.Cell(r, c++).Value = DrugTestReasons.Label(t.Alasan);
                ws.Cell(r, c++).Value = t.TesUrin ? t.UrinHasil ?? "" : "-";
                foreach (var p in paramNames)
                {
                    var item = t.UrineItems.FirstOrDefault(i => i.ParameterName == p);
                    var cell = ws.Cell(r, c++);
                    cell.Value = item?.Hasil ?? "";
                    if (item?.Hasil == TestResults.Positif) cell.Style.Fill.BackgroundColor = bad;
                }
                if (t.AlkoholKadar.HasValue) ws.Cell(r, c).Value = t.AlkoholKadar.Value;
                c++;
                if (t.AlkoholBatas.HasValue) ws.Cell(r, c).Value = t.AlkoholBatas.Value;
                c++;
                ws.Cell(r, c++).Value = t.TesAlkohol ? t.AlkoholHasil ?? "" : "-";
                var akhir = ws.Cell(r, c++);
                akhir.Value = t.HasilAkhir;
                if (t.IsPositive) akhir.Style.Fill.BackgroundColor = bad;
                ws.Cell(r, c++).Value = t.Catatan ?? "";
                ws.Cell(r, c++).Value = t.AttachmentCount;
                ws.Cell(r, c++).Value = t.ExaminerName;
                r++;
            }

            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(4).Style.NumberFormat.Format = "@";
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            ws.Range(1, 1, Math.Max(r - 1, 1), headers.Count).SetAutoFilter();
            ws.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Tes_Urin_Alkohol_{_clock.Today:yyyyMMdd}.xlsx");
        }

        private DrugTestQuery BuildQuery(DateTime? from, DateTime? to, string? department, string? hasil, string? jenis, string? kelompok) => new()
        {
            Kelompok = kelompok,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Department = department,
            Hasil = hasil,
            Jenis = jenis
        };
    }
}
