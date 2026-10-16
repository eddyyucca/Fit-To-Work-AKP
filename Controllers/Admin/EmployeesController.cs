using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers.Admin
{
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MasterEmployees)]
    [Route("Admin/Employees")]
    public class EmployeesController : Controller
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IFitToWorkRepository _ftwRepository;
        private readonly IGymRepository _gymRepository;
        private readonly IPermissionService _permissionService;
        private readonly IDashboardNotifier _notifier;
        private readonly IEmployeeImportRepository _importRepository;
        private readonly IMemoryCache _cache;
        private readonly IHrisEmployeeSync _hrisSync;

        public EmployeesController(IEmployeeRepository employeeRepository, IOrganizationRepository orgRepository,
            IFitToWorkRepository ftwRepository, IGymRepository gymRepository, IPermissionService permissionService,
            IDashboardNotifier notifier, IEmployeeImportRepository importRepository, IMemoryCache cache,
            IHrisEmployeeSync hrisSync)
        {
            _hrisSync = hrisSync;
            _importRepository = importRepository;
            _cache = cache;
            _employeeRepository = employeeRepository;
            _orgRepository = orgRepository;
            _ftwRepository = ftwRepository;
            _gymRepository = gymRepository;
            _permissionService = permissionService;
            _notifier = notifier;
        }

        // Setiap perubahan data karyawan (simpan, aktif/nonaktif, import, hapus) mengubah jumlah
        // karyawan aktif di Dashboard -> kirim sinyal realtime supaya dashboard memuat ulang.
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            var failed = executed.Exception is not null
                         || executed.Result is IStatusCodeActionResult { StatusCode: >= 400 };
            if (HttpMethods.IsPost(Request.Method) && !failed)
                await _notifier.NotifyRefreshAllAsync();
        }

        private async Task LoadOrganizationDropdownsAsync()
        {
            ViewBag.DivisionOptions = (await _orgRepository.GetDivisionsAsync(onlyActive: true)).ToList();
            ViewBag.DepartmentOptions = (await _orgRepository.GetDepartmentsAsync(onlyActive: true)).ToList();
            ViewBag.SectionOptions = (await _orgRepository.GetSectionsAsync(onlyActive: true)).ToList();
            ViewBag.PositionOptions = (await _orgRepository.GetPositionsAsync(onlyActive: true)).ToList();
        }

        // Halaman daftar karyawan. Tabelnya kosong saat halaman dibuka; isinya diambil
        // per halaman lewat endpoint Data (server-side DataTables), jadi tidak semua
        // karyawan dimuat sekaligus. Dropdown organisasi dimuat sekali untuk modal form.
        [HttpGet("")]
        public async Task<IActionResult> Index(string? search, int? edit)
        {
            await LoadOrganizationDropdownsAsync();
            ViewBag.Search = search;
            ViewBag.EditId = edit;
            ViewBag.TotalKaryawan = await _employeeRepository.CountAsync();
            return View();
        }

        // ---------------- API untuk DataTables & modal form (dipanggil dari wwwroot/js/employees.js) ----------------

        // GET Admin/Employees/Data?draw=1&start=0&length=25&search[value]=...&order[0][column]=1&order[0][dir]=asc&status=active
        // Format request & response mengikuti protokol "serverSide" DataTables.
        [HttpGet("Data")]
        public async Task<IActionResult> Data(int draw, int start, int length, string? status)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);

            var query = new EmployeePageQuery
            {
                Search = Request.Query["search[value]"].ToString(),
                IsActive = status switch { "active" => true, "inactive" => false, _ => null },
                SortColumn = sortColumn,
                SortDesc = string.Equals(Request.Query["order[0][dir]"], "desc", StringComparison.OrdinalIgnoreCase),
                Skip = start,
                Take = length
            };

            var page = await _employeeRepository.GetPageAsync(query);

            return Json(new
            {
                draw,                               // dikembalikan apa adanya supaya DataTables tahu respons milik request yang mana
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                data = page.Rows.Select(e => new
                {
                    id = e.Id,
                    nip = e.NIP,
                    nama = e.Nama,
                    division = e.DivisionName,
                    department = e.DepartmentName,
                    section = e.SectionName,
                    position = e.PositionName,
                    nomorHp = e.NomorHp,
                    isActive = e.IsActive
                })
            });
        }

        // GET Admin/Employees/Export?status=active&search=...&sort=1&dir=asc
        // Kolom SAMA dengan template import (A-J), jadi file hasil download bisa diedit lalu di-import ulang.
        [HttpGet("Export")]
        public async Task<IActionResult> Export(string? status, string? search, int sort = 1, string? dir = "asc")
        {
            var rows = await _employeeRepository.GetForExportAsync(new EmployeePageQuery
            {
                Search = search,
                IsActive = status switch { "active" => true, "inactive" => false, _ => null },
                SortColumn = sort,
                SortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
            });

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Data Karyawan");
            string[] headers =
            {
                "NIP", "Nama", "NomorHp", "Division", "Department", "Section", "Position", "Aktif (Y/N)",
                "Tanggal Lahir (dd/mm/yyyy)", "Jenis Kelamin (L/P)"
            };
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            var head = ws.Range(1, 1, 1, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B3A6B");

            var r = 2;
            foreach (var e in rows)
            {
                ws.Cell(r, 1).Value = e.NIP;
                ws.Cell(r, 2).Value = e.Nama;
                ws.Cell(r, 3).Value = e.NomorHp ?? string.Empty;
                ws.Cell(r, 4).Value = e.DivisionName ?? string.Empty;
                ws.Cell(r, 5).Value = e.DepartmentName ?? string.Empty;
                ws.Cell(r, 6).Value = e.SectionName ?? string.Empty;
                ws.Cell(r, 7).Value = e.PositionName ?? string.Empty;
                ws.Cell(r, 8).Value = e.IsActive ? "Y" : "N";
                if (e.TanggalLahir.HasValue) ws.Cell(r, 9).Value = e.TanggalLahir.Value.Date;
                ws.Cell(r, 10).Value = e.JenisKelamin ?? string.Empty;
                if (!e.IsActive)
                    ws.Range(r, 1, r, headers.Length).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
                r++;
            }

            // NIP & No. HP sebagai teks supaya angka 0 di depan tidak hilang
            ws.Column(1).Style.NumberFormat.Format = "@";
            ws.Column(3).Style.NumberFormat.Format = "@";
            ws.Column(9).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Columns().AdjustToContents(1, Math.Min(r, 300));
            ws.Range(1, 1, Math.Max(r - 1, 1), headers.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            var suffix = status switch { "active" => "_Aktif", "inactive" => "_Nonaktif", _ => "" };
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Data_Karyawan{suffix}_{DateTime.UtcNow.AddHours(8):yyyyMMdd}.xlsx");
        }

        // GET Admin/Employees/Detail/5 -> isi modal Detail Karyawan (HTML partial, dimuat lewat AJAX).
        // Riwayat FTW / Gym / kesehatan hanya tampil kalau admin juga punya hak akses menu tersebut.
        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id)
        {
            var e = await _employeeRepository.GetByIdAsync(id);
            if (e is null)
                return NotFound("Karyawan tidak ditemukan (mungkin sudah dihapus).");

            var access = await _permissionService.GetAsync(User);
            var vm = new EmployeeDetailViewModel
            {
                Employee = e,
                Summary = await _employeeRepository.GetActivitySummaryAsync(id),
                CanSeeFtw = access.Has(Permissions.FtwHistory) || access.Has(Permissions.FtwDashboard),
                CanSeeGym = access.Has(Permissions.GymHistory),
                CanSeeMedical = access.Has(Permissions.MedicalCheckup) || access.Has(Permissions.MedicalVisit)
            };
            if (vm.CanSeeFtw)
                vm.Ftw = await _ftwRepository.GetByEmployeeAsync(id, 30);
            if (vm.CanSeeGym)
                vm.Gym = await _gymRepository.GetByEmployeeAsync(id, 30);

            return PartialView("_DetailContent", vm);
        }

        // GET Admin/Employees/Get/5 -> isi modal form edit
        [HttpGet("Get/{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var e = await _employeeRepository.GetByIdAsync(id);
            if (e is null)
                return NotFound(new { message = "Karyawan tidak ditemukan (mungkin sudah dihapus)." });

            return Json(new
            {
                id = e.Id,
                nip = e.NIP,
                nama = e.Nama,
                nomorHp = e.NomorHp,
                divisionId = e.DivisionId,
                departmentId = e.DepartmentId,
                sectionId = e.SectionId,
                positionId = e.PositionId,
                tanggalLahir = e.TanggalLahir?.ToString("yyyy-MM-dd"),
                jenisKelamin = e.JenisKelamin,
                isActive = e.IsActive
            });
        }

        // POST Admin/Employees/Save -> simpan dari modal (Id = 0 berarti tambah baru, selain itu edit)
        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(EmployeeFormViewModel model)
        {
            var isEdit = model.Id > 0;
            Employee? employee = null;

            if (isEdit)
            {
                employee = await _employeeRepository.GetByIdAsync(model.Id);
                if (employee is null)
                    return NotFound(new { message = "Karyawan tidak ditemukan (mungkin sudah dihapus)." });
                if (employee.IsMitra)
                    return BadRequest(new { message = "Karyawan mitra diubah lewat menu Karyawan Mitra." });
            }

            var nip = (model.NIP ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(nip))
            {
                var duplicate = await _employeeRepository.GetByNIPAsync(nip);
                if (duplicate is not null && duplicate.Id != model.Id)
                    ModelState.AddModelError(nameof(model.NIP), $"NIP sudah dipakai oleh {duplicate.Nama}");
            }

            if (model.TanggalLahir.HasValue &&
                (model.TanggalLahir.Value.Date > DateTime.UtcNow.Date || model.TanggalLahir.Value.Year < 1930))
                ModelState.AddModelError(nameof(model.TanggalLahir), "Tanggal lahir tidak valid");

            if (!ModelState.IsValid)
            {
                // Kirim pesan error per field supaya bisa ditampilkan di bawah input yang salah
                var errors = ModelState
                    .Where(kv => kv.Value!.Errors.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return BadRequest(new { message = "Periksa kembali isian yang ditandai merah.", errors });
            }

            employee ??= new Employee();
            employee.NIP = nip;
            employee.Nama = model.Nama.Trim();
            employee.NomorHp = string.IsNullOrWhiteSpace(model.NomorHp) ? null : model.NomorHp.Trim();
            employee.DivisionId = model.DivisionId;
            employee.DepartmentId = model.DepartmentId;
            employee.SectionId = model.SectionId;
            employee.PositionId = model.PositionId;
            employee.TanggalLahir = model.TanggalLahir?.Date;
            employee.JenisKelamin = string.IsNullOrWhiteSpace(model.JenisKelamin) ? null : model.JenisKelamin;
            employee.IsActive = model.IsActive;

            if (isEdit)
                await _employeeRepository.UpdateAsync(employee);
            else
                await _employeeRepository.CreateAsync(employee);

            return Json(new
            {
                message = isEdit
                    ? $"Data karyawan {employee.Nama} berhasil diperbarui."
                    : $"Karyawan {employee.Nama} berhasil ditambahkan."
            });
        }

        // POST Admin/Employees/ResetPortalPassword/5 -> password Riwayat Saya kembali ke tanggal lahir (ddMMyyyy).
        // Dipakai kalau karyawan lupa password. Karyawan wajib membuat password baru saat login berikutnya.
        // Tercatat di Log Akses (label di AccessLogger).
        [HttpPost("ResetPortalPassword/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPortalPassword(int id)
        {
            var employee = await _employeeRepository.GetByIdAsync(id);
            if (employee is null)
                return NotFound(new { message = "Karyawan tidak ditemukan." });
            if (employee.IsMitra)
                return BadRequest(new { message = "Karyawan mitra tidak memakai Riwayat Saya." });

            await _employeeRepository.ResetPortalPasswordAsync(id);
            return Json(new
            {
                message = employee.TanggalLahir.HasValue
                    ? $"Password Riwayat Saya {employee.Nama} di-reset ke tanggal lahir ({PortalPasswordRules.DefaultPassword(employee.TanggalLahir.Value)}). Karyawan wajib membuat password baru saat login."
                    : $"Password Riwayat Saya {employee.Nama} di-reset, tapi tanggal lahirnya belum diisi: lengkapi tanggal lahir supaya karyawan bisa login."
            });
        }

        // POST Admin/Employees/SetActive/5 -> tombol Aktifkan/Nonaktifkan di tabel (tanpa reload halaman)
        [HttpPost("SetActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetActive(int id, bool activate)
        {
            var employee = await _employeeRepository.GetByIdAsync(id);
            if (employee is null)
                return NotFound(new { message = "Karyawan tidak ditemukan." });

            await _employeeRepository.SetActiveAsync(id, activate);
            return Json(new
            {
                message = activate
                    ? $"{employee.Nama} diaktifkan kembali."
                    : $"{employee.Nama} dinonaktifkan."
            });
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            await LoadOrganizationDropdownsAsync();
            return View(new EmployeeFormViewModel { IsEdit = false, IsActive = true });
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmployeeFormViewModel model)
        {
            model.IsEdit = false;

            var existing = await _employeeRepository.GetByNIPAsync(model.NIP.Trim());
            if (existing is not null)
                ModelState.AddModelError(nameof(model.NIP), "NIP sudah terdaftar");

            if (!ModelState.IsValid)
            {
                await LoadOrganizationDropdownsAsync();
                return View(model);
            }

            var employee = new Employee
            {
                NIP = model.NIP.Trim(),
                Nama = model.Nama.Trim(),
                NomorHp = model.NomorHp,
                DivisionId = model.DivisionId,
                DepartmentId = model.DepartmentId,
                SectionId = model.SectionId,
                PositionId = model.PositionId,
                TanggalLahir = model.TanggalLahir?.Date,
                JenisKelamin = model.JenisKelamin,
                IsActive = model.IsActive
            };

            await _employeeRepository.CreateAsync(employee);
            TempData["Success"] = $"Karyawan {employee.Nama} berhasil ditambahkan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _employeeRepository.GetByIdAsync(id);
            if (employee is null)
                return NotFound();

            var model = new EmployeeFormViewModel
            {
                Id = employee.Id,
                NIP = employee.NIP,
                Nama = employee.Nama,
                NomorHp = employee.NomorHp,
                DivisionId = employee.DivisionId,
                DepartmentId = employee.DepartmentId,
                SectionId = employee.SectionId,
                PositionId = employee.PositionId,
                IsActive = employee.IsActive,
                IsEdit = true
            };

            await LoadOrganizationDropdownsAsync();
            return View(model);
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EmployeeFormViewModel model)
        {
            model.IsEdit = true;
            model.Id = id;

            var employee = await _employeeRepository.GetByIdAsync(id);
            if (employee is null || employee.IsMitra)
                return NotFound();

            var duplicateNip = await _employeeRepository.GetByNIPAsync(model.NIP.Trim());
            if (duplicateNip is not null && duplicateNip.Id != id)
                ModelState.AddModelError(nameof(model.NIP), "NIP sudah dipakai karyawan lain");

            if (!ModelState.IsValid)
            {
                await LoadOrganizationDropdownsAsync();
                return View(model);
            }

            employee.NIP = model.NIP.Trim();
            employee.Nama = model.Nama.Trim();
            employee.NomorHp = model.NomorHp;
            employee.DivisionId = model.DivisionId;
            employee.DepartmentId = model.DepartmentId;
            employee.SectionId = model.SectionId;
            employee.PositionId = model.PositionId;
            employee.IsActive = model.IsActive;

            await _employeeRepository.UpdateAsync(employee);

            TempData["Success"] = $"Data karyawan {employee.Nama} berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            await _employeeRepository.SetActiveAsync(id, activate);
            TempData["Success"] = activate ? "Karyawan diaktifkan kembali." : "Karyawan dinonaktifkan.";
            return RedirectToAction(nameof(Index));
        }

        // ---------------- Hapus SEMUA data karyawan (destruktif, sengaja dibuat sulit dipicu tanpa sengaja) ----------------

        private const string DeleteAllConfirmationText = "HAPUS SEMUA";

        [HttpPost("DeleteAll")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll(string? confirmText)
        {
            if (!string.Equals(confirmText?.Trim(), DeleteAllConfirmationText, StringComparison.Ordinal))
            {
                TempData["Error"] = $"Konfirmasi tidak sesuai. Anda harus mengetik persis \"{DeleteAllConfirmationText}\" untuk melanjutkan. Tidak ada data yang dihapus.";
                return RedirectToAction(nameof(Index));
            }

            var jumlahSebelum = await _employeeRepository.CountAsync();
            try
            {
                await _employeeRepository.DeleteAllAsync();
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = $"Berhasil menghapus semua data karyawan ({jumlahSebelum} karyawan) beserta seluruh riwayat pengisian Fit To Work mereka.";
            return RedirectToAction(nameof(Index));
        }

        // ---------------- Sinkron karyawan dari HRIS (REST API) ----------------

        [HttpGet("HrisSync")]
        public IActionResult HrisSync()
        {
            ViewBag.IsConfigured = _hrisSync.IsConfigured;
            return View();
        }

        [HttpPost("HrisSync")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HrisSync(bool dryRun, CancellationToken ct)
        {
            ViewBag.IsConfigured = _hrisSync.IsConfigured;
            try
            {
                return View(await _hrisSync.RunAsync(dryRun, ct));
            }
            catch (HrisSyncException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View();
            }
        }

        // ---------------- Import Data Karyawan dari Excel ----------------

        [HttpGet("Import")]
        public IActionResult Import()
        {
            return View();
        }

        [HttpGet("Import/Template")]
        public async Task<IActionResult> DownloadTemplate()
        {
            // Ambil master data aktif untuk dijadikan referensi + dropdown di Excel
            var divisions = (await _orgRepository.GetDivisionsAsync(onlyActive: true)).ToList();
            var departments = (await _orgRepository.GetDepartmentsAsync(onlyActive: true)).ToList();
            var sections = (await _orgRepository.GetSectionsAsync(onlyActive: true)).ToList();
            var positions = (await _orgRepository.GetPositionsAsync(onlyActive: true)).ToList();

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Data Karyawan");

            sheet.Cell(1, 1).Value = "NIP";
            sheet.Cell(1, 2).Value = "Nama";
            sheet.Cell(1, 3).Value = "NomorHp";
            sheet.Cell(1, 4).Value = "Division";
            sheet.Cell(1, 5).Value = "Department";
            sheet.Cell(1, 6).Value = "Section";
            sheet.Cell(1, 7).Value = "Position";
            sheet.Cell(1, 8).Value = "Aktif (Y/N)";
            sheet.Cell(1, 9).Value = "Tanggal Lahir (dd/mm/yyyy)";
            sheet.Cell(1, 10).Value = "Jenis Kelamin (L/P)";
            sheet.Row(1).Style.Font.Bold = true;

            sheet.Cell(2, 1).Value = "20001234";
            sheet.Cell(2, 2).Value = "Budi Santoso";
            sheet.Cell(2, 3).Value = "081234567890";
            sheet.Cell(2, 4).Value = divisions.FirstOrDefault()?.Name ?? "Umum";
            sheet.Cell(2, 5).Value = departments.FirstOrDefault()?.Name ?? "Umum";
            sheet.Cell(2, 6).Value = sections.FirstOrDefault()?.Name ?? "PRODUCTION";
            sheet.Cell(2, 7).Value = positions.FirstOrDefault()?.Name ?? "Staff";
            sheet.Cell(2, 8).Value = "Y";
            sheet.Cell(2, 9).Value = new DateTime(1990, 5, 17);
            sheet.Cell(2, 9).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(2, 10).Value = "L";

            // ---------------- Sheet "Master Data" ----------------
            var masterSheet = workbook.Worksheets.Add("Master Data");

            masterSheet.Cell(1, 1).Value = "Division";
            masterSheet.Cell(1, 2).Value = "Department";
            masterSheet.Cell(1, 3).Value = "Section";
            masterSheet.Cell(1, 4).Value = "Position";
            masterSheet.Row(1).Style.Font.Bold = true;

            for (int i = 0; i < divisions.Count; i++)
                masterSheet.Cell(i + 2, 1).Value = divisions[i].Name;
            for (int i = 0; i < departments.Count; i++)
                masterSheet.Cell(i + 2, 2).Value = departments[i].Name;
            for (int i = 0; i < sections.Count; i++)
                masterSheet.Cell(i + 2, 3).Value = sections[i].Name;
            for (int i = 0; i < positions.Count; i++)
                masterSheet.Cell(i + 2, 4).Value = positions[i].Name;

            masterSheet.Columns().AdjustToContents();

            var divisionListRange = masterSheet.Range(2, 1, 1 + Math.Max(divisions.Count, 1), 1);
            var departmentListRange = masterSheet.Range(2, 2, 1 + Math.Max(departments.Count, 1), 2);
            var sectionListRange = masterSheet.Range(2, 3, 1 + Math.Max(sections.Count, 1), 3);
            var positionListRange = masterSheet.Range(2, 4, 1 + Math.Max(positions.Count, 1), 4);

            // ---------------- Dropdown di sheet "Data Karyawan" ----------------
            const int maxDataRows = 500;

            sheet.Range(2, 4, maxDataRows + 1, 4).CreateDataValidation().List(divisionListRange, true);
            sheet.Range(2, 5, maxDataRows + 1, 5).CreateDataValidation().List(departmentListRange, true);
            sheet.Range(2, 6, maxDataRows + 1, 6).CreateDataValidation().List(sectionListRange, true);
            sheet.Range(2, 7, maxDataRows + 1, 7).CreateDataValidation().List(positionListRange, true);

            var aktifValidation = sheet.Range(2, 8, maxDataRows + 1, 8).CreateDataValidation();
            aktifValidation.List("Y,N", true);
            sheet.Range(2, 10, maxDataRows + 1, 10).CreateDataValidation().List("L,P", true);
            sheet.Range(2, 9, maxDataRows + 1, 9).Style.DateFormat.Format = "dd/MM/yyyy";

            sheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Data_Karyawan.xlsx");
        }

        // Hasil baca file disimpan sementara (per admin + token) sampai admin klik Simpan di pratinjau
        private static readonly TimeSpan ImportPreviewLifetime = TimeSpan.FromMinutes(30);
        private string ImportCacheKey(string token) => $"employee-import:{User.AdminId()}:{token}";

        /// <summary>
        /// Langkah 1: baca Excel → halaman PRATINJAU (belum ada yang disimpan).
        /// Division / Department / Jabatan yang tidak dikenali tidak membuat baris gagal; admin memilih
        /// di pratinjau: tambah baru, pakai yang ada (perbaiki), aktifkan lagi, atau kosongkan.
        /// </summary>
        [HttpPost("Import")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            if (file is null || file.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "Pilih file Excel (.xlsx) terlebih dahulu.");
                return View();
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
            {
                ModelState.AddModelError(string.Empty, "Format file harus .xlsx atau .xls.");
                return View();
            }

            // Master organisasi diambil SEKALI (aktif & nonaktif: yang nonaktif bisa ditawarkan "aktifkan lagi")
            static List<OrgItem> Items<T>(IEnumerable<T> list, Func<T, OrgItem> map) => list.Select(map).ToList();
            var masters = new Dictionary<OrgKind, List<OrgItem>>
            {
                [OrgKind.Division] = Items(await _orgRepository.GetDivisionsAsync(), d => new OrgItem(d.Id, d.Name, d.IsActive)),
                [OrgKind.Department] = Items(await _orgRepository.GetDepartmentsAsync(), d => new OrgItem(d.Id, d.Name, d.IsActive)),
                [OrgKind.Position] = Items(await _orgRepository.GetPositionsAsync(), p => new OrgItem(p.Id, p.Name, p.IsActive))
            };
            var activeSections = (await _orgRepository.GetSectionsAsync(onlyActive: true)).ToList();

            var preview = new EmployeeImportPreview
            {
                Token = Guid.NewGuid().ToString("N"),
                FileName = Path.GetFileName(file.FileName),
                Masters = masters
            };

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                using var workbook = new XLWorkbook(stream);
                var sheet = workbook.Worksheet(1);
                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
                var firstRowOfNip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Baris 1 = header, data mulai baris 2.
                // Kolom: A=NIP, B=Nama, C=NomorHp, D=Division, E=Department, F=Section, G=Position (Jabatan),
                //        H=Aktif (Y/N, default Y), I=Tanggal Lahir, J=Jenis Kelamin (L/P)
                for (int r = 2; r <= lastRow; r++)
                {
                    var nip = sheet.Cell(r, 1).GetString().Trim();
                    var nama = EmployeeImportRules.Clean(sheet.Cell(r, 2).GetString()) ?? string.Empty;

                    // Lewati baris yang benar-benar kosong (misal sisa baris kosong di bawah data)
                    if (string.IsNullOrWhiteSpace(nip) && string.IsNullOrWhiteSpace(nama))
                        continue;

                    var nomorHp = sheet.Cell(r, 3).GetString().Trim();
                    var sectionText = EmployeeImportRules.Clean(sheet.Cell(r, 6).GetString());
                    var aktifRaw = sheet.Cell(r, 8).GetString().Trim();
                    var (tanggalLahir, tanggalLahirError) = ReadDate(sheet.Cell(r, 9));
                    var jkRaw = sheet.Cell(r, 10).GetString().Trim().ToUpperInvariant();
                    var jenisKelamin = jkRaw switch { "L" or "LAKI-LAKI" or "PRIA" => "L", "P" or "PEREMPUAN" or "WANITA" => "P", _ => null };
                    var section = sectionText is null ? null
                        : activeSections.FirstOrDefault(s => EmployeeImportRules.Key(s.Name) == EmployeeImportRules.Key(sectionText));

                    var row = new EmployeeImportRow
                    {
                        RowNumber = r,
                        NIP = nip,
                        Nama = nama,
                        NomorHp = string.IsNullOrWhiteSpace(nomorHp) ? null : nomorHp,
                        DivisionText = EmployeeImportRules.Clean(sheet.Cell(r, 4).GetString()),
                        DepartmentText = EmployeeImportRules.Clean(sheet.Cell(r, 5).GetString()),
                        PositionText = EmployeeImportRules.Clean(sheet.Cell(r, 7).GetString()),
                        SectionId = section?.Id ?? 0,
                        SectionName = section?.Name ?? sectionText,
                        TanggalLahir = tanggalLahir,
                        JenisKelamin = jenisKelamin,
                        IsActive = string.IsNullOrWhiteSpace(aktifRaw) || aktifRaw.Equals("Y", StringComparison.OrdinalIgnoreCase) || aktifRaw.Equals("YA", StringComparison.OrdinalIgnoreCase)
                    };

                    if (string.IsNullOrWhiteSpace(nip))
                        row.Error = "NIP kosong";
                    else if (nip.Length > 20)
                        row.Error = "NIP maksimal 20 karakter";
                    else if (firstRowOfNip.TryGetValue(nip, out var firstRow))
                        row.Error = $"NIP sama dengan baris {firstRow} di file ini";
                    else if (string.IsNullOrWhiteSpace(nama))
                        row.Error = "Nama kosong";
                    else if (sectionText is null)
                        row.Error = "Section kosong";
                    else if (section is null)
                        row.Error = $"Section '{sectionText}' tidak dikenali/tidak aktif (cek menu Data Organisasi)";
                    else if (tanggalLahirError is not null)
                        row.Error = tanggalLahirError;
                    else if (!string.IsNullOrEmpty(jkRaw) && jenisKelamin is null)
                        row.Error = $"Jenis kelamin '{jkRaw}' tidak dikenali (isi L atau P)";

                    if (!string.IsNullOrWhiteSpace(nip) && !firstRowOfNip.ContainsKey(nip))
                        firstRowOfNip[nip] = r;

                    preview.Rows.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ModelState.AddModelError(string.Empty, "File tidak bisa dibaca sebagai Excel. Pastikan memakai template dari tombol Unduh Template. Detail: " + ex.Message);
                return View();
            }

            // NIP yang sudah ada → diperbarui; milik karyawan mitra → ditolak
            var existing = await _importRepository.GetExistingNipsAsync(preview.Rows.Where(r => r.IsValid).Select(r => r.NIP));
            foreach (var row in preview.Rows.Where(r => r.IsValid))
            {
                if (existing.TryGetValue(row.NIP, out var isMitra) && isMitra)
                    row.Error = "NIP ini milik karyawan mitra (kelola di menu Karyawan Mitra)";
                else
                    row.IsNew = !existing.ContainsKey(row.NIP);
            }

            // Cocokkan Division / Department / Jabatan; yang tidak cocok dikumpulkan untuk diputuskan admin
            var valid = preview.Rows.Where(r => r.IsValid).ToList();
            foreach (var row in valid)
            {
                row.DivisionId = EmployeeImportRules.FindActive(masters[OrgKind.Division], row.DivisionText);
                row.DepartmentId = EmployeeImportRules.FindActive(masters[OrgKind.Department], row.DepartmentText);
                row.PositionId = EmployeeImportRules.FindActive(masters[OrgKind.Position], row.PositionText);
            }
            foreach (var kind in new[] { OrgKind.Position, OrgKind.Division, OrgKind.Department })
                preview.Unknowns.AddRange(EmployeeImportRules.FindUnknowns(kind, valid.Select(r => (r.RowNumber, r.TextOf(kind))), masters[kind]));

            _cache.Set(ImportCacheKey(preview.Token), preview, ImportPreviewLifetime);
            return View("ImportPreview", preview);
        }

        /// <summary>
        /// Langkah 2: terapkan keputusan admin untuk nama yang tidak dikenali, lalu simpan semua baris valid
        /// dalam SATU transaksi. Baris yang gagal di pratinjau dilewati.
        /// </summary>
        [HttpPost("Import/Confirm")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportConfirm(string? token, List<string?> act, List<int?> map)
        {
            if (string.IsNullOrEmpty(token) || !_cache.TryGetValue(ImportCacheKey(token), out EmployeeImportPreview? preview) || preview is null)
            {
                TempData["Error"] = "Pratinjau import sudah kedaluwarsa atau sudah disimpan. Silakan upload ulang file.";
                return RedirectToAction(nameof(Import));
            }

            IActionResult ShowPreviewAgain()
            {
                ViewBag.Act = act;
                ViewBag.Map = map;
                return View("ImportPreview", preview);
            }

            if (preview.TotalValid == 0)
            {
                ModelState.AddModelError(string.Empty, "Tidak ada baris yang bisa disimpan. Perbaiki file lalu upload ulang.");
                return ShowPreviewAgain();
            }

            var commit = new EmployeeImportCommit { Rows = preview.Rows };
            for (var i = 0; i < preview.Unknowns.Count; i++)
            {
                var u = preview.Unknowns[i];
                var choice = EmployeeImportRules.ParseChoice(u, act.ElementAtOrDefault(i), map.ElementAtOrDefault(i), preview.Masters[u.Kind], out var error);
                if (choice is null)
                    ModelState.AddModelError(string.Empty, error!);
                else
                    commit.Decisions[(u.Kind, u.Key)] = (u, choice);
            }
            if (!ModelState.IsValid)
                return ShowPreviewAgain();

            EmployeeImportCommitResult saved;
            try
            {
                saved = await _importRepository.CommitAsync(commit);
            }
            catch (EmployeeImportException ex)
            {
                ModelState.AddModelError(string.Empty, "Import dibatalkan, tidak ada data yang tersimpan. " + ex.Message);
                return ShowPreviewAgain();
            }
            catch (System.Data.Common.DbException ex)
            {
                ModelState.AddModelError(string.Empty, "Import dibatalkan, tidak ada data yang tersimpan. Detail: " + ex.Message);
                return ShowPreviewAgain();
            }

            _cache.Remove(ImportCacheKey(token));

            var result = new ImportEmployeeResultViewModel { OrgNotes = saved.OrgNotes };
            result.Rows.AddRange(saved.Rows);
            result.Rows.AddRange(preview.Rows.Where(r => !r.IsValid).Select(r => new ImportEmployeeRowResult
            {
                RowNumber = r.RowNumber,
                NIP = r.NIP,
                Nama = r.Nama,
                Message = r.Error!
            }));
            result.Rows.Sort((a, b) => a.RowNumber.CompareTo(b.RowNumber));

            if (result.TotalSuccess > 0)
                TempData["Success"] = $"{result.TotalSuccess} dari {result.TotalRows} baris berhasil disimpan ({result.TotalCreated} baru, {result.TotalUpdated} diperbarui).";

            return View("ImportResult", result);
        }

        // Tanggal di Excel bisa berupa tipe tanggal asli atau teks (dd/mm/yyyy, d-m-yyyy, yyyy-mm-dd).
        private static (DateTime? Value, string? Error) ReadDate(IXLCell cell)
        {
            if (cell.IsEmpty())
                return (null, null);

            if (cell.DataType == XLDataType.DateTime)
                return Validate(cell.GetDateTime().Date);

            if (cell.DataType == XLDataType.Number)
            {
                try { return Validate(DateTime.FromOADate(cell.GetDouble()).Date); }
                catch { return (null, "Tanggal lahir tidak valid"); }
            }

            var text = cell.GetString().Trim();
            string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd.MM.yyyy" };
            if (DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                return Validate(parsed.Date);

            return (null, $"Tanggal lahir '{text}' tidak dikenali (pakai format dd/mm/yyyy)");

            static (DateTime?, string?) Validate(DateTime d) =>
                d.Year < 1930 || d > DateTime.UtcNow.Date ? (null, $"Tanggal lahir {d:dd/MM/yyyy} tidak masuk akal") : (d, null);
        }
}
}