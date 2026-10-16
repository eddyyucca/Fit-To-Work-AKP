using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    [Authorize(Policy = "EmployeeOnly")]
    public class FormController : Controller
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IFitToWorkRepository _fitToWorkRepository;
        private readonly IFormOptionRepository _formOptionRepository;
        private readonly IDashboardNotifier _dashboardNotifier;
        private readonly ILocationGuard _locationGuard;
        private readonly IFtwSettingRepository _ftwSettings;
        private readonly IAppClock _clock;

        public FormController(
            IEmployeeRepository employeeRepository,
            IFitToWorkRepository fitToWorkRepository,
            IFormOptionRepository formOptionRepository,
            IDashboardNotifier dashboardNotifier,
            ILocationGuard locationGuard,
            IFtwSettingRepository ftwSettings,
            IAppClock clock)
        {
            _employeeRepository = employeeRepository;
            _fitToWorkRepository = fitToWorkRepository;
            _formOptionRepository = formOptionRepository;
            _dashboardNotifier = dashboardNotifier;
            _locationGuard = locationGuard;
            _ftwSettings = ftwSettings;
            _clock = clock;
        }

        private int CurrentEmployeeId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Ambil semua daftar pilihan (radio/checkbox/dropdown) dari database sekaligus,
        // supaya cukup 1x panggil sebelum render form.
        private async Task LoadFormOptionsIntoViewBagAsync()
        {
            ViewBag.JadwalKerjaOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.JadwalKerja)).ToList();
            ViewBag.AreaKerjaOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.AreaKerja)).ToList();
            ViewBag.JamTidurOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.JamTidur)).ToList();
            ViewBag.KualitasTidurOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.KualitasTidur)).ToList();
            ViewBag.GejalaOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.GejalaKesehatan)).ToList();
            ViewBag.KondisiFokusOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.KondisiFokus)).ToList();
            ViewBag.KesiapanKerjaOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.KesiapanKerja)).ToList();
        }

        /// <summary>
        /// Pengisian terakhir karyawan kalau masih di periode yang sama (aturan dari menu Pengaturan Pengisian),
        /// beserta model halaman "Sudah mengisi". Null = boleh mengisi.
        /// </summary>
        private async Task<FtwAlreadySubmittedViewModel?> GetLockedPeriodAsync(int employeeId, FtwSettings settings)
        {
            var latest = await _fitToWorkRepository.GetLatestForEmployeeAsync(employeeId);
            if (latest is null)
                return null;

            var lastLocal = _clock.ToLocal(latest.CreatedAt);
            var nowLocal = _clock.LocalNow;
            if (!FtwRules.IsLocked(lastLocal, nowLocal, settings))
                return null;

            var nextOpen = FtwRules.NextOpenLocal(lastLocal, settings);
            return new FtwAlreadySubmittedViewModel
            {
                Submission = latest,
                NextOpenLocal = nextOpen,
                NextOpenText = FtwRules.FormatNextOpen(nextOpen, nowLocal),
                IzinIsiUlang = settings.IzinIsiUlang,
                DetikKembali = settings.DetikKembali
            };
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool force = false)
        {
            var employee = await _employeeRepository.GetByIdAsync(CurrentEmployeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            // Kalau sudah mengisi di periode ini (jeda / jam reset di menu Pengaturan Pengisian), cukup
            // tampilkan notifikasi (bukan form lagi) supaya tidak dobel isi tanpa sengaja. Kalau kondisi
            // berubah dan "Isi Ulang" diizinkan, karyawan klik tombolnya (?force=true).
            var settings = await _ftwSettings.GetAsync();
            var locked = await GetLockedPeriodAsync(employee.Id, settings);
            if (locked is not null && !(force && settings.IzinIsiUlang))
            {
                return View("AlreadySubmitted", locked);
            }

            var vm = new FitToWorkFormViewModel
            {
                EmployeeId = employee.Id,
                Nama = employee.Nama,
                Section = employee.SectionName ?? string.Empty,
                NomorHp = employee.NomorHp ?? string.Empty
            };

            await LoadFormOptionsIntoViewBagAsync();
            ViewBag.Location = await _locationGuard.GetUiAsync(HttpContext);

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(FitToWorkFormViewModel model, string? geoLat, string? geoLng, string? geoAcc)
        {
            // Data diri & section selalu diambil ulang dari database (bukan dari form),
            // supaya tidak bisa dimanipulasi lewat request meski field-nya read-only di UI.
            var employee = await _employeeRepository.GetByIdAsync(CurrentEmployeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            // Isi Ulang dimatikan admin: tolak di server juga (tombolnya bisa saja di-bypass lewat URL/request)
            var settings = await _ftwSettings.GetAsync();
            if (!settings.IzinIsiUlang && await GetLockedPeriodAsync(employee.Id, settings) is not null)
                return RedirectToAction(nameof(Index));

            // Kunci lokasi: dicek di server walau browser sudah mengecek (JavaScript bisa diakali)
            var lokasi = await _locationGuard.CheckAsync(HttpContext, geoLat, geoLng, geoAcc);
            if (!lokasi.Allowed)
                ModelState.AddModelError(string.Empty, lokasi.Message);

            if (model.AreaKerja == "Other" && string.IsNullOrWhiteSpace(model.AreaKerjaLainnya))
                ModelState.AddModelError(nameof(model.AreaKerjaLainnya), "Sebutkan area kerja Anda");

            if (model.IsFit)
            {
                // FIT: detail kondisi tidak ditampilkan di form, jadi tidak wajib & disimpan "-"
                foreach (var key in new[] { nameof(model.KualitasTidur), nameof(model.KondisiFokus), nameof(model.KonsumsiObat), nameof(model.GejalaKesehatan) })
                    ModelState.Remove(key);
                model.KualitasTidur = FitToWorkFormViewModel.TidakDitanyakan;
                model.KondisiFokus = FitToWorkFormViewModel.TidakDitanyakan;
                model.KonsumsiObat = FitToWorkFormViewModel.TidakDitanyakan;
                model.GejalaKesehatan = new List<string> { FitToWorkFormViewModel.TidakDitanyakan };
            }
            else if (model.GejalaKesehatan.Count == 0)
                ModelState.AddModelError(nameof(model.GejalaKesehatan), "Pilih gejala yang dirasakan atau 'TIDAK ADA GEJALA'");

            if (!ModelState.IsValid)
            {
                model.Nama = employee.Nama;
                model.Section = employee.SectionName ?? string.Empty;
                await LoadFormOptionsIntoViewBagAsync();
                ViewBag.Location = await _locationGuard.GetUiAsync(HttpContext);   // pesan lokasi tampil di ringkasan error
                return View(model);
            }

            var submission = new FitToWorkSubmission
            {
                EmployeeId = employee.Id,
                NamaSnapshot = employee.Nama,
                SectionSnapshot = employee.SectionName ?? string.Empty,
                NomorHp = model.NomorHp,
                JadwalKerja = model.JadwalKerja,
                AreaKerja = model.AreaKerja,
                AreaKerjaLainnya = model.AreaKerja == "Other" ? model.AreaKerjaLainnya : null,
                JamTidur = model.JamTidur,
                KualitasTidur = model.KualitasTidur,
                GejalaKesehatan = string.Join(", ", model.GejalaKesehatan),
                KondisiFokus = model.KondisiFokus,
                KonsumsiObat = model.KonsumsiObat,
                KesiapanKerja = model.KesiapanKerja
            };
            lokasi.StampTo(submission);

            var id = await _fitToWorkRepository.CreateAsync(submission);

            // Kirim notifikasi realtime ke dashboard admin (toastr + refresh chart)
            await _dashboardNotifier.NotifySubmissionAsync(id);

            return RedirectToAction("Success", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Success(int id)
        {
            var submission = await _fitToWorkRepository.GetByIdAsync(id);
            if (submission is null || submission.EmployeeId != CurrentEmployeeId)
                return RedirectToAction("Index");

            return View(submission);
        }
    }
}
