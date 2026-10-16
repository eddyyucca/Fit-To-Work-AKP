using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    /// <summary>
    /// Aktivitas Kebugaran karyawan (dulu: Gym): pilih jenis aktivitas -> Mulai -> Selesai -> durasi (+ jarak GPS) tercatat.
    /// URL tetap /Gym supaya tautan & shortcut lama di HP karyawan tetap jalan.
    /// Aturan (lihat Services/GymRules.cs & Services/FitnessTrack.cs):
    ///  - Boleh beberapa sesi per hari, tapi aktivitas baru hanya setelah sesi sebelumnya diselesaikan.
    ///  - Tidak diselesaikan sampai batas jam jenis aktivitas -> ditutup otomatis "Tidak diselesaikan".
    ///  - Kunci lokasi per jenis: mulai & selesai di site (gym), hanya mulai (lari, hiking), atau tanpa kunci.
    ///  - Aktivitas ber-GPS: titik GPS direkam di HP, jarak dihitung ulang di server saat selesai.
    /// </summary>
    [Authorize(Policy = "EmployeeOnly")]
    public class GymController : Controller
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IGymRepository _gymRepository;
        private readonly IActivityTypeRepository _typeRepository;
        private readonly IDashboardNotifier _dashboardNotifier;
        private readonly ILocationGuard _locationGuard;
        private readonly IAppClock _clock;
        private readonly ILogger<GymController> _logger;

        public GymController(IEmployeeRepository employeeRepository, IGymRepository gymRepository,
            IActivityTypeRepository typeRepository, IDashboardNotifier dashboardNotifier, ILocationGuard locationGuard,
            IAppClock clock, ILogger<GymController> logger)
        {
            _clock = clock;
            _employeeRepository = employeeRepository;
            _gymRepository = gymRepository;
            _typeRepository = typeRepository;
            _dashboardNotifier = dashboardNotifier;
            _locationGuard = locationGuard;
            _logger = logger;
        }

        private int CurrentEmployeeId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task<GymFormViewModel> ToFormAsync(Employee employee, GymFormViewModel? posted = null) => new()
        {
            Nama = employee.Nama,
            NIP = employee.NIP,
            Department = employee.DepartmentName ?? string.Empty,
            Position = employee.PositionName ?? string.Empty,
            Types = await _typeRepository.GetActiveAsync(),
            ActivityTypeId = posted?.ActivityTypeId,
            BeratBadan = posted?.BeratBadan
        };

        private async Task<IActionResult> TrackView(GymAttendance open, string? error = null)
        {
            // Lokasi hanya dicek saat selesai untuk aktivitas "mulai & selesai di site" (mis. gym)
            ViewBag.Location = ActivityLocationLock.CheckAtFinish(open.KunciLokasi)
                ? await _locationGuard.GetUiAsync(HttpContext)
                : new LocationUiModel { Mode = LocationUiModel.ModeOff };
            ViewBag.Error = error;
            if (open.IsGps)
            {
                // Batas kecepatan yang sama dipakai di HP (tampilan) dan di server (penyimpanan)
                var type = open.ActivityTypeId is int t ? await _typeRepository.GetByIdAsync(t) : null;
                ViewBag.MaxSpeedKmh = (double?)type?.MaksKecepatanKmh ?? FitnessTrack.DefaultMaxSpeedKmh;
            }
            return View("CheckOut", open);
        }

        private async Task<IActionResult> StartView(GymFormViewModel form)
        {
            ViewBag.Location = await _locationGuard.GetUiAsync(HttpContext);
            return View("Index", form);
        }

        // GET /Gym -> pilih aktivitas, atau halaman aktivitas berjalan kalau masih ada sesi berlangsung
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var employee = await _employeeRepository.GetByIdAsync(CurrentEmployeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            var open = await _gymRepository.GetOpenAsync(employee.Id);
            if (open is not null)
                return await TrackView(open);

            return await StartView(await ToFormAsync(employee));
        }

        // POST /Gym -> mulai aktivitas
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(GymFormViewModel model, string? geoLat, string? geoLng, string? geoAcc)
        {
            // Data diri selalu diambil ulang dari database (bukan dari form) supaya tidak bisa dimanipulasi.
            var employee = await _employeeRepository.GetByIdAsync(CurrentEmployeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            // Masih ada sesi berlangsung (tombol ditekan dua kali / tab lama) -> wajib selesaikan dulu
            var open = await _gymRepository.GetOpenAsync(employee.Id);
            if (open is not null)
                return await TrackView(open, $"Anda masih tercatat sedang {open.Aktivitas}. Selesaikan dulu sebelum mulai aktivitas lain.");

            var type = model.ActivityTypeId is int typeId ? await _typeRepository.GetByIdAsync(typeId) : null;
            if (model.ActivityTypeId is not null && (type is null || !type.IsActive))
                ModelState.AddModelError(nameof(model.ActivityTypeId), "Jenis aktivitas tidak tersedia. Pilih yang lain.");

            if (type is { WajibBerat: true } && model.BeratBadan is null)
                ModelState.AddModelError(nameof(model.BeratBadan), $"Berat badan wajib diisi untuk {type.Nama}");
            else if (type is { WajibBerat: false })
                ModelState.Remove(nameof(model.BeratBadan));   // tidak dipakai, jadi isinya tidak perlu divalidasi

            GeofenceResult? lokasi = null;
            if (type is not null && ActivityLocationLock.CheckAtStart(type.KunciLokasi))
            {
                lokasi = await _locationGuard.CheckAsync(HttpContext, geoLat, geoLng, geoAcc);
                if (!lokasi.Allowed)
                    ModelState.AddModelError(string.Empty, lokasi.Message);
            }

            if (!ModelState.IsValid || type is null)
                return await StartView(await ToFormAsync(employee, model));

            var attendance = new GymAttendance
            {
                EmployeeId = employee.Id,
                NamaSnapshot = employee.Nama,
                DepartmentSnapshot = employee.DepartmentName ?? string.Empty,
                SectionSnapshot = employee.SectionName ?? string.Empty,
                PositionSnapshot = employee.PositionName ?? string.Empty,
                // Berat hanya disimpan untuk jenis yang mewajibkan (gym), supaya data berat tetap konsisten
                BeratBadan = type.WajibBerat ? Math.Round(model.BeratBadan!.Value, 1) : null,
                ActivityTypeId = type.Id,
                AktivitasNama = type.Nama,
                AktivitasIkon = type.Ikon,
                PakaiGps = type.PakaiGps,
                KunciLokasi = type.KunciLokasi
            };
            lokasi?.StampTo(attendance);

            int id;
            try
            {
                id = await _gymRepository.CreateAsync(attendance, type.MaksJam);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Index UX_GymAttendances_Open: mulai dobel yang masuk bersamaan
                return RedirectToAction(nameof(Index));
            }

            await _dashboardNotifier.NotifyGymAsync(employee.Nama, $"mulai {type.Nama}");   // dashboard ter-update realtime

            // Aktivitas ber-GPS langsung ke layar rekam (perekaman jarak dimulai di halaman itu)
            return type.PakaiGps
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Success), new { id });
        }

        // POST /Gym/CheckOut/5 -> selesaikan aktivitas (track = titik GPS dari HP, JSON [[lat,lng,akurasi,unixMs],...])
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(ValueLengthLimit = 2 * 1024 * 1024)]
        public async Task<IActionResult> CheckOut(int id, string? track, string? geoLat, string? geoLng, string? geoAcc)
        {
            var employee = await _employeeRepository.GetByIdAsync(CurrentEmployeeId);
            if (employee is null || !employee.IsActive)
                return RedirectToAction("EmployeeLogin", "Account");

            var open = await _gymRepository.GetOpenAsync(employee.Id);
            if (open is null || open.Id != id)
            {
                // Sudah selesai (tombol ditekan dua kali) atau sudah lewat batas dan ditutup otomatis
                var session = await _gymRepository.GetByIdAsync(id);
                if (session is not null && session.EmployeeId == employee.Id && session.CheckOutStatus == GymCheckOutStatus.Manual)
                    return RedirectToAction(nameof(Finished), new { id });

                TempData["Info"] = session is not null && session.EmployeeId == employee.Id
                    ? $"Sesi {session.Aktivitas} sebelumnya sudah ditutup otomatis karena melewati batas waktu " +
                      $"(pukul {_clock.ToLocal(GymRules.DeadlineUtc(session)):HH:mm}) dan dicatat \"Tidak diselesaikan\". Silakan mulai lagi."
                    : "Sesi aktivitas tidak ditemukan. Silakan mulai lagi.";
                return RedirectToAction(nameof(Index));
            }

            int? jarakKeSite = null;
            if (ActivityLocationLock.CheckAtFinish(open.KunciLokasi))
            {
                var lokasi = await _locationGuard.CheckAsync(HttpContext, geoLat, geoLng, geoAcc);
                if (!lokasi.Allowed)
                    return await TrackView(open, lokasi.Message);
                jarakKeSite = lokasi.DistanceMeter;
            }

            GymTrackSave? save = null;
            if (open.IsGps)
                save = await BuildTrackAsync(open, track);

            if (!await _gymRepository.CheckOutAsync(open.Id, employee.Id, jarakKeSite, save))
                return RedirectToAction(nameof(Index));

            await _dashboardNotifier.NotifyGymAsync(employee.Nama, $"selesai {open.Aktivitas}");
            return RedirectToAction(nameof(Finished), new { id = open.Id });
        }

        // Hitung ulang jarak dari titik mentah (angka dari HP tidak dipercaya begitu saja)
        private async Task<GymTrackSave> BuildTrackAsync(GymAttendance open, string? track)
        {
            var type = open.ActivityTypeId is int t ? await _typeRepository.GetByIdAsync(t) : null;
            var points = FitnessTrack.Parse(track, open.CreatedAt, DateTime.UtcNow);
            if (points.Count == 0)
            {
                _logger.LogInformation("Aktivitas #{Id}: selesai tanpa titik GPS", open.Id);
                return new GymTrackSave(null, null, 0, null);
            }

            var result = FitnessTrack.Compute(points, (double?)type?.MaksKecepatanKmh);
            var route = FitnessTrack.Simplify(result.Accepted);
            return new GymTrackSave(
                result.DistanceMeter,
                route.Count >= 2 ? FitnessTrack.EncodePolyline(route) : null,
                result.PointsUsed,
                result.GapSeconds);
        }

        // Setelah mulai (aktivitas tanpa GPS)
        [HttpGet]
        public async Task<IActionResult> Success(int id)
        {
            var attendance = await _gymRepository.GetByIdAsync(id);
            if (attendance is null || attendance.EmployeeId != CurrentEmployeeId)
                return RedirectToAction(nameof(Index));

            ViewBag.Mode = "checkin";
            return View("Done", attendance);
        }

        // Setelah selesai: durasi, jarak, pace & peta rute
        [HttpGet]
        public async Task<IActionResult> Finished(int id)
        {
            var attendance = await _gymRepository.GetByIdAsync(id);
            if (attendance is null || attendance.EmployeeId != CurrentEmployeeId || attendance.CheckOutStatus != GymCheckOutStatus.Manual)
                return RedirectToAction(nameof(Index));

            ViewBag.Mode = "checkout";
            return View("Done", attendance);
        }
    }
}
