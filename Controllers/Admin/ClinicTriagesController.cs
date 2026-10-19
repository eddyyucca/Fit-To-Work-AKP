using System.ComponentModel.DataAnnotations;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class TriageFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Pilih karyawan")]
        public int? EmployeeId { get; set; }

        /// <summary>Pengisian FTW tidak fit yang menjadi asal pemeriksaan awal ini (dari tombol di Laporan FTW &gt; Tindak Lanjut).</summary>
        public int? FtwSubmissionId { get; set; }

        [Required(ErrorMessage = "Waktu pemeriksaan wajib diisi")]
        public DateTime? TriageAt { get; set; }            // waktu lokal site

        [Required(ErrorMessage = "Keluhan wajib diisi")]
        [StringLength(1000)]
        public string? Keluhan { get; set; }

        [Range(50, 300, ErrorMessage = "Tekanan sistolik 50-300 mmHg")]
        public int? Sistolik { get; set; }

        [Range(30, 200, ErrorMessage = "Tekanan diastolik 30-200 mmHg")]
        public int? Diastolik { get; set; }

        [Range(20, 250, ErrorMessage = "Nadi 20-250 x/menit")]
        public int? Nadi { get; set; }

        [Range(30, 45, ErrorMessage = "Suhu 30-45 °C")]
        public decimal? Suhu { get; set; }

        [Range(5, 80, ErrorMessage = "Frekuensi napas 5-80 x/menit")]
        public int? Napas { get; set; }

        [Range(50, 100, ErrorMessage = "SpO2 50-100 %")]
        public int? Spo2 { get; set; }

        [Range(1, 400, ErrorMessage = "Berat badan 1-400 kg")]
        public decimal? BeratBadan { get; set; }

        [StringLength(1000)]
        public string? CatatanPerawat { get; set; }

        public string? EmployeeLabel { get; set; }
    }

    /// <summary>
    /// Pemeriksaan awal (triage) oleh perawat: keluhan + tanda vital disimpan sebagai DRAFT, lalu dokter melanjutkannya
    /// di form Kunjungan Klinik (diagnosa ICD-10, obat, rekomendasi) dan memfinalkannya.
    /// Permission: medical.triage (perawat) atau medical.visit (dokter). Melanjutkan/memfinalkan butuh medical.visit.
    /// Draft BUKAN kunjungan klinik: tidak masuk dashboard / laporan dan tidak memotong stok sampai difinalkan.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalTriage, Permissions.MedicalVisit)]
    [Route("Admin/ClinicTriages")]
    public class ClinicTriagesController : Controller
    {
        private const int DefaultRangeDays = 7;

        private readonly IClinicTriageRepository _repo;
        private readonly IFitToWorkRepository _ftwRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IPermissionService _permissions;
        private readonly IAppClock _clock;

        public ClinicTriagesController(IClinicTriageRepository repo, IFitToWorkRepository ftwRepository, IEmployeeRepository employeeRepository,
            IPermissionService permissions, IAppClock clock)
        {
            _repo = repo;
            _ftwRepository = ftwRepository;
            _employeeRepository = employeeRepository;
            _permissions = permissions;
            _clock = clock;
        }

        // ============================ Daftar ============================

        // Default: yang masih Draft (semua tanggal, supaya draft lama tidak terlewat). Status lain memakai rentang tanggal.
        [HttpGet("")]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? status, string? search)
        {
            var today = _clock.Today;
            var fromD = (from ?? today.AddDays(-(DefaultRangeDays - 1))).Date;
            var toD = (to ?? today).Date;
            if (fromD > toD) (fromD, toD) = (toD, fromD);

            var st = (status ?? "draft").Trim().ToLowerInvariant();
            string? statusDb = st switch { "draft" => TriageStatuses.Draft, "selesai" => TriageStatuses.Selesai, "batal" => TriageStatuses.Batal, _ => null };
            if (statusDb is null) st = "semua";
            var ignoreRange = statusDb == TriageStatuses.Draft;

            var rows = await _repo.GetListAsync(new ClinicTriageQuery
            {
                Status = statusDb,
                FromUtc = ignoreRange ? null : _clock.ToUtc(fromD),
                ToUtc = ignoreRange ? null : _clock.ToUtc(toD.AddDays(1)),
                Search = search
            });

            ViewBag.From = fromD;
            ViewBag.To = toD;
            ViewBag.Status = st;
            ViewBag.Search = search;
            ViewBag.RangeIgnored = ignoreRange;
            ViewBag.CanDoctor = (await _permissions.GetAsync(User)).Has(Permissions.MedicalVisit);
            ViewBag.DraftCount = await _repo.CountDraftAsync();
            return View(rows);
        }

        // ============================ Buat / ubah draft ============================

        [HttpGet("Create")]
        public async Task<IActionResult> Create(int? employeeId, int? ftwId)
        {
            var model = new TriageFormViewModel { TriageAt = CheckupsController.TrimToMinute(_clock.LocalNow) };

            // Dari Laporan FTW > Tindak Lanjut: pasien dan keluhan terisi dari hasil screening Fit To Work yang tidak fit
            if (ftwId.HasValue)
            {
                var ftw = await _ftwRepository.GetByIdAsync(ftwId.Value);
                if (ftw is null)
                {
                    TempData["Error"] = "Pengisian Fit To Work tidak ditemukan.";
                    return RedirectToAction("FollowUps", "FtwReports");
                }
                // Sudah ada pemeriksaan awal untuk kejadian ini: buka yang ada, jangan buat ganda
                var existing = await _repo.GetActiveByFtwSubmissionAsync(ftw.Id);
                if (existing is not null)
                {
                    TempData["Success"] = "Kejadian Fit To Work ini sudah punya pemeriksaan awal. Berikut statusnya.";
                    return RedirectToAction(nameof(Detail), new { id = existing.Id });
                }
                if (await _employeeRepository.GetByIdAsync(ftw.EmployeeId) is { IsActive: true } fe)
                {
                    model.EmployeeId = fe.Id;
                    model.EmployeeLabel = $"{fe.NIP} - {fe.Nama}";
                    model.FtwSubmissionId = ftw.Id;
                    model.Keluhan = BuildKeluhanFromFtw(ftw);
                    return View("Form", model);
                }
            }

            if (employeeId.HasValue && await _employeeRepository.GetByIdAsync(employeeId.Value) is { IsActive: true } e)
            {
                model.EmployeeId = e.Id;
                model.EmployeeLabel = $"{e.NIP} - {e.Nama}";
            }
            return View("Form", model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TriageFormViewModel model)
        {
            var employee = model.EmployeeId.HasValue ? await _employeeRepository.GetByIdAsync(model.EmployeeId.Value) : null;
            if (model.EmployeeId.HasValue && (employee is null || !employee.IsActive))
                ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan tidak ditemukan atau tidak aktif");
            Validate(model);

            // Tautan ke pengisian FTW: harus ada, milik karyawan yang sama, dan belum punya pemeriksaan awal
            if (model.FtwSubmissionId.HasValue)
            {
                var ftw = await _ftwRepository.GetByIdAsync(model.FtwSubmissionId.Value);
                if (ftw is null || ftw.EmployeeId != model.EmployeeId)
                    ModelState.AddModelError(string.Empty, "Pengisian Fit To Work tidak cocok dengan pasien yang dipilih");
                else if (await _repo.GetActiveByFtwSubmissionAsync(ftw.Id) is { } dup)
                {
                    TempData["Error"] = "Kejadian Fit To Work ini sudah punya pemeriksaan awal.";
                    return RedirectToAction(nameof(Detail), new { id = dup.Id });
                }
            }

            if (ModelState.IsValid)
            {
                var t = new ClinicTriage
                {
                    EmployeeId = employee!.Id,
                    FtwSubmissionId = model.FtwSubmissionId,
                    NamaSnapshot = employee.Nama,
                    // Karyawan mitra: department = nama mitra, section = "Mitra Kerja", posisi = jabatan di mitra (sama seperti kunjungan klinik)
                    DepartmentSnapshot = employee.UnitKerja,
                    SectionSnapshot = employee.BagianKerja,
                    PositionSnapshot = employee.JabatanKerja,
                    NurseAdminId = User.AdminId(),
                    NurseName = User.AdminName()
                };
                Apply(t, model);
                var id = await _repo.CreateAsync(t);
                TempData["Success"] = $"Pemeriksaan awal {employee.Nama} tersimpan sebagai draft. Menunggu dokter.";
                return RedirectToAction(nameof(Detail), new { id });
            }

            if (employee is not null) model.EmployeeLabel = $"{employee.NIP} - {employee.Nama}";
            return View("Form", model);
        }

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null) return NotFound();
            if (!t.IsDraft)
            {
                TempData["Error"] = "Hanya pemeriksaan awal berstatus draft yang bisa diubah.";
                return RedirectToAction(nameof(Detail), new { id });
            }
            return View("Form", ToForm(t));
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TriageFormViewModel model)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null) return NotFound();
            model.Id = id;
            model.EmployeeId = t.EmployeeId;      // pasien tidak bisa diganti; salah pasien = batalkan lalu buat baru
            ModelState.Remove(nameof(model.EmployeeId));
            model.EmployeeLabel = $"{t.NIP} - {t.NamaSnapshot}";
            Validate(model);

            if (ModelState.IsValid)
            {
                Apply(t, model);
                try
                {
                    await _repo.UpdateDraftAsync(t);
                    TempData["Success"] = "Draft pemeriksaan awal diperbarui.";
                    return RedirectToAction(nameof(Detail), new { id });
                }
                catch (TriageStateException ex)
                {
                    TempData["Error"] = ex.Message;
                    return RedirectToAction(nameof(Detail), new { id });
                }
            }

            return View("Form", model);
        }

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
                await _repo.CancelAsync(id, reason.Trim(), User.AdminName());
                TempData["Success"] = "Pemeriksaan awal dibatalkan.";
            }
            catch (TriageStateException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id });
        }

        // ============================ Detail ============================

        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null) return NotFound();
            ViewBag.CanDoctor = (await _permissions.GetAsync(User)).Has(Permissions.MedicalVisit);
            return View(t);
        }

        // ============================ Cari karyawan (autocomplete) ============================

        // Sama seperti MedicalRecord/SearchEmployee, tapi memakai hak akses perawat (medical.triage) agar perawat tidak perlu
        // akses ke halaman Rekam Medis. Karyawan AKP dan mitra ikut dicari.
        [HttpGet("SearchEmployee")]
        public async Task<IActionResult> SearchEmployee(string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(Array.Empty<object>());

            var today = _clock.Today;
            var employees = await _employeeRepository.GetAllAsync(q.Trim(), EmployeeScope.All);
            return Json(employees.Where(e => e.IsActive).Take(15).Select(e => new
            {
                id = e.Id,
                nip = e.NIP,
                nama = e.Nama,
                department = e.UnitKerja,
                section = e.BagianKerja,
                position = e.JabatanKerja,
                mitra = e.IsMitra,
                jenisKelamin = e.JenisKelamin,
                umur = e.TanggalLahir.HasValue ? AgeOn(e.TanggalLahir.Value, today) : (int?)null
            }));
        }

        // ============================ Pembantu ============================

        /// <summary>Teks keluhan awal dari jawaban screening FTW yang tidak fit (perawat bisa mengubahnya).</summary>
        private static string BuildKeluhanFromFtw(FitToWorkSubmission f)
        {
            var parts = new List<string> { "Hasil Fit To Work: TIDAK FIT." };
            if (!string.IsNullOrWhiteSpace(f.GejalaKesehatan) && f.GejalaKesehatan != "-")
                parts.Add($"Gejala: {f.GejalaKesehatan}.");
            parts.Add($"Tidur {f.JamTidur}" + (f.KualitasTidur is { Length: > 0 } k && k != "-" ? $", kualitas: {k}" : "") + ".");
            if (!string.IsNullOrWhiteSpace(f.KondisiFokus) && f.KondisiFokus != "-")
                parts.Add($"Fokus: {f.KondisiFokus}.");
            if (!string.IsNullOrWhiteSpace(f.KonsumsiObat) && f.KonsumsiObat != "-")
                parts.Add($"Konsumsi obat 12 jam terakhir: {f.KonsumsiObat}.");
            var text = string.Join(" ", parts);
            return text.Length <= 1000 ? text : text[..1000];
        }

        private static int AgeOn(DateTime birth, DateTime on)
        {
            var age = on.Year - birth.Year;
            if (birth.Date > on.AddYears(-age)) age--;
            return age;
        }

        private void Validate(TriageFormViewModel model)
        {
            if (model.Sistolik.HasValue && model.Diastolik.HasValue && model.Sistolik <= model.Diastolik)
                ModelState.AddModelError(nameof(model.Sistolik), "Tekanan sistolik harus lebih besar dari diastolik");
            if (model.Sistolik.HasValue != model.Diastolik.HasValue)
                ModelState.AddModelError(nameof(model.Sistolik), "Isi tekanan darah lengkap (sistolik dan diastolik) atau kosongkan keduanya");
            if (model.TriageAt.HasValue && model.TriageAt.Value > _clock.LocalNow.AddMinutes(10))
                ModelState.AddModelError(nameof(model.TriageAt), "Waktu pemeriksaan tidak boleh di masa depan");
        }

        private void Apply(ClinicTriage t, TriageFormViewModel m)
        {
            t.TriageAt = _clock.ToUtc(m.TriageAt!.Value);
            t.Keluhan = m.Keluhan!.Trim();
            t.Sistolik = m.Sistolik;
            t.Diastolik = m.Diastolik;
            t.Nadi = m.Nadi;
            t.Suhu = m.Suhu;
            t.Napas = m.Napas;
            t.Spo2 = m.Spo2;
            t.BeratBadan = m.BeratBadan;
            t.CatatanPerawat = string.IsNullOrWhiteSpace(m.CatatanPerawat) ? null : m.CatatanPerawat.Trim();
        }

        private TriageFormViewModel ToForm(ClinicTriage t) => new()
        {
            Id = t.Id,
            EmployeeId = t.EmployeeId,
            EmployeeLabel = $"{t.NIP} - {t.NamaSnapshot}",
            TriageAt = _clock.ToLocal(t.TriageAt),
            Keluhan = t.Keluhan,
            Sistolik = t.Sistolik,
            Diastolik = t.Diastolik,
            Nadi = t.Nadi,
            Suhu = t.Suhu,
            Napas = t.Napas,
            Spo2 = t.Spo2,
            BeratBadan = t.BeratBadan,
            CatatanPerawat = t.CatatanPerawat
        };
    }
}
