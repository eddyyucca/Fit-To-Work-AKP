using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers
{
    public class PortalLoginViewModel
    {
        [Required(ErrorMessage = "NIP wajib diisi")]
        public string NIP { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password wajib diisi")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class PortalChangePasswordViewModel
    {
        // Diisi hanya kalau ganti password lewat menu (bukan saat wajib ganti setelah login pertama)
        [DataType(DataType.Password)]
        public string? PasswordLama { get; set; }

        [Required(ErrorMessage = "Password baru wajib diisi")]
        [DataType(DataType.Password)]
        public string PasswordBaru { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ulangi password baru")]
        [DataType(DataType.Password)]
        public string UlangiPassword { get; set; } = string.Empty;

        public bool Wajib { get; set; }   // true = masih password default, belum boleh membuka riwayat
    }

    /// <summary>
    /// "Riwayat Saya": karyawan melihat riwayat FTW, aktivitas kebugaran, pemeriksaan kesehatan & kunjungan klinik MILIK SENDIRI.
    /// Login NIP + password (berbeda dari layar FTW yang cukup NIP), sesi 30 menit, dibatasi percobaan login.
    /// Password awal = tanggal lahir (ddMMyyyy). Login dengan password awal diberi role "PortalPending":
    /// hanya boleh membuka halaman Ganti Password, sampai password diganti (lalu role menjadi "Portal").
    /// </summary>
    [Route("Portal")]
    public class PortalController : Controller
    {
        public const string RolePortal = "Portal";
        public const string RolePending = "PortalPending";

        private const int MaxFailPerNip = 5;
        private const int MaxFailPerIp = 20;
        private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan SessionLength = TimeSpan.FromMinutes(30);

        private readonly IEmployeeRepository _employeeRepository;
        private readonly IFitToWorkRepository _ftwRepository;
        private readonly IGymRepository _gymRepository;
        private readonly IHealthCheckupRepository _checkupRepository;
        private readonly IClinicVisitRepository _visitRepository;
        private readonly IAccessLogger _accessLogger;
        private readonly IMemoryCache _cache;
        private readonly IMedicalAttachmentRepository _attachmentRepository;
        private readonly IMedicalFileStorage _fileStorage;

        public PortalController(IEmployeeRepository employeeRepository, IFitToWorkRepository ftwRepository,
            IGymRepository gymRepository, IHealthCheckupRepository checkupRepository, IClinicVisitRepository visitRepository,
            IAccessLogger accessLogger, IMemoryCache cache, IMedicalAttachmentRepository attachmentRepository,
            IMedicalFileStorage fileStorage)
        {
            _attachmentRepository = attachmentRepository;
            _fileStorage = fileStorage;
            _employeeRepository = employeeRepository;
            _ftwRepository = ftwRepository;
            _gymRepository = gymRepository;
            _checkupRepository = checkupRepository;
            _visitRepository = visitRepository;
            _accessLogger = accessLogger;
            _cache = cache;
        }

        [HttpGet("Login")]
        [AllowAnonymous]
        public IActionResult Login(string? nip = null)
        {
            if (User.IsInRole(RolePortal))
                return RedirectToAction(nameof(Index));
            if (User.IsInRole(RolePending))
                return RedirectToAction(nameof(ChangePassword));   // mis. mencoba membuka riwayat sebelum ganti password
            return View(new PortalLoginViewModel { NIP = nip ?? string.Empty });
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(PortalLoginViewModel model)
        {
            model.NIP = model.NIP?.Trim() ?? string.Empty;
            if (!ModelState.IsValid)
                return View(model);

            var ip = AccessLogger.GetClientIp(HttpContext) ?? "unknown";
            var nipKey = NipFailKey(model.NIP);
            var ipKey = $"portal-fail:ip:{ip}";

            if (FailCount(nipKey) >= MaxFailPerNip || FailCount(ipKey) >= MaxFailPerIp)
            {
                ModelState.AddModelError(string.Empty, "Terlalu banyak percobaan gagal. Coba lagi 15 menit lagi atau hubungi admin/HR.");
                return View(model);
            }

            var employee = await _employeeRepository.GetByNIPAsync(model.NIP);
            var allowed = employee is not null && employee.IsActive && !employee.IsMitra;
            var check = PortalLoginCheck.Wrong;
            if (allowed)
            {
                var hash = await _employeeRepository.GetPortalPasswordHashAsync(employee!.Id);
                check = PortalPasswordRules.Check(model.Password, hash, employee.TanggalLahir, PasswordHasher.Verify);
            }

            if (check is PortalLoginCheck.Wrong or PortalLoginCheck.NoBirthDate)
            {
                AddFail(nipKey);
                AddFail(ipKey);
                await _accessLogger.LogAsync(AccessLogEvents.PortalLoginFailed,
                    employee is null ? "Riwayat Saya: NIP tidak ditemukan"
                        : !allowed ? "Riwayat Saya: karyawan nonaktif / mitra"
                        : check == PortalLoginCheck.NoBirthDate ? "Riwayat Saya: tanggal lahir belum terdaftar (password default tidak ada)"
                        : "Riwayat Saya: password salah",
                    StatusCodes.Status200OK, username: $"NIP {model.NIP}");

                ModelState.AddModelError(string.Empty, check == PortalLoginCheck.NoBirthDate
                    ? "Tanggal lahir Anda belum terdaftar di sistem, jadi password awal belum ada. Hubungi HR/admin untuk melengkapi data."
                    : "NIP atau password tidak cocok.");
                model.Password = string.Empty;
                return View(model);
            }

            _cache.Remove(nipKey);

            var mustChange = check == PortalLoginCheck.OkMustChange;
            await SignInPortalAsync(employee!, mustChange ? RolePending : RolePortal);

            await _accessLogger.LogAsync(AccessLogEvents.PortalLogin,
                mustChange ? "Riwayat Saya: login dengan password awal (wajib ganti password)" : "Riwayat Saya: login berhasil",
                StatusCodes.Status302Found, username: $"NIP {employee!.NIP} ({employee.Nama})");

            return mustChange ? RedirectToAction(nameof(ChangePassword)) : RedirectToAction(nameof(Index));
        }

        // GET Portal/ChangePassword : wajib (setelah login dengan password awal) atau lewat tombol "Ganti Password"
        [HttpGet("ChangePassword")]
        [Authorize(Roles = RolePortal + "," + RolePending)]
        public async Task<IActionResult> ChangePassword()
        {
            var employee = await CurrentEmployeeAsync();
            if (employee is null)
                return await ForceLogoutAsync();

            ViewBag.Employee = employee;
            return View(new PortalChangePasswordViewModel { Wajib = User.IsInRole(RolePending) });
        }

        [HttpPost("ChangePassword")]
        [Authorize(Roles = RolePortal + "," + RolePending)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(PortalChangePasswordViewModel model)
        {
            var employee = await CurrentEmployeeAsync();
            if (employee is null)
                return await ForceLogoutAsync();

            // Status "wajib" diambil dari sesi, bukan dari form (supaya tidak bisa dilewati)
            model.Wajib = User.IsInRole(RolePending);
            ViewBag.Employee = employee;

            var nipKey = NipFailKey(employee.NIP);
            if (FailCount(nipKey) >= MaxFailPerNip)
            {
                ModelState.AddModelError(string.Empty, "Terlalu banyak percobaan gagal. Coba lagi 15 menit lagi atau hubungi admin/HR.");
                return View(Cleared(model));
            }

            if (!ModelState.IsValid)
                return View(Cleared(model));

            var hash = await _employeeRepository.GetPortalPasswordHashAsync(employee.Id);

            // Ganti lewat menu: password lama wajib benar
            if (!model.Wajib)
            {
                var oldCheck = PortalPasswordRules.Check(model.PasswordLama, hash, employee.TanggalLahir, PasswordHasher.Verify);
                if (oldCheck is PortalLoginCheck.Wrong or PortalLoginCheck.NoBirthDate)
                {
                    AddFail(nipKey);
                    await _accessLogger.LogAsync(AccessLogEvents.PortalLoginFailed, "Riwayat Saya: ganti password, password lama salah",
                        StatusCodes.Status200OK, username: $"NIP {employee.NIP} ({employee.Nama})");
                    ModelState.AddModelError(nameof(model.PasswordLama), "Password lama salah.");
                    return View(Cleared(model));
                }
            }
            // Sesi "wajib ganti" tapi password ternyata sudah diganti (mis. di perangkat lain) -> login ulang
            else if (!string.IsNullOrEmpty(hash))
            {
                return await ForceLogoutAsync();
            }

            var error = PortalPasswordRules.ValidateNew(model.PasswordBaru, model.UlangiPassword, employee.NIP, employee.TanggalLahir);
            if (error is null && !string.IsNullOrEmpty(hash) && PasswordHasher.Verify(model.PasswordBaru, hash))
                error = "Password baru harus berbeda dari password lama.";
            if (error is not null)
            {
                ModelState.AddModelError(nameof(model.PasswordBaru), error);
                return View(Cleared(model));
            }

            await _employeeRepository.SetPortalPasswordAsync(employee.Id, PasswordHasher.Hash(model.PasswordBaru));
            _cache.Remove(nipKey);

            // Sesi lama (role PortalPending) diganti sesi penuh
            await SignInPortalAsync(employee, RolePortal);
            await _accessLogger.LogAsync(AccessLogEvents.PortalPasswordChanged,
                model.Wajib ? "Riwayat Saya: password awal diganti" : "Riwayat Saya: password diganti",
                StatusCodes.Status302Found, username: $"NIP {employee.NIP} ({employee.Nama})");

            TempData["PortalMessage"] = "Password berhasil diganti. Gunakan password baru ini untuk login Riwayat Saya berikutnya.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("")]
        [Authorize(Policy = "PortalOnly")]
        public async Task<IActionResult> Index()
        {
            var employeeId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null || !employee.IsActive || employee.IsMitra)
                return await ForceLogoutAsync();

            var checkups = await _checkupRepository.GetByEmployeeAsync(employeeId);
            ViewBag.Checkups = checkups;
            ViewBag.CheckupAttachments = await _attachmentRepository.GetForEntitiesAsync(
                AttachmentEntityTypes.Checkup, checkups.Where(c => c.AttachmentCount > 0).Select(c => c.Id));
            ViewBag.Visits = await _visitRepository.GetByEmployeeAsync(employeeId, includeCancelled: false);
            ViewBag.Ftw = await _ftwRepository.GetByEmployeeAsync(employeeId, 60);
            ViewBag.Gym = await _gymRepository.GetByEmployeeAsync(employeeId, 60);
            return View(employee);
        }

        // GET Portal/Route/5 : peta rute aktivitas MILIK karyawan yang login saja
        [HttpGet("Route/{id:int}")]
        [Authorize(Policy = "PortalOnly")]
        public async Task<IActionResult> Route(int id, [FromServices] IAppClock clock)
        {
            var employeeId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var g = await _gymRepository.GetByIdAsync(id);
            if (g is null || g.EmployeeId != employeeId)
                return NotFound();
            return Json(Admin.GymHistoryController.RouteJson(g, clock, withEmployee: false));
        }

        // GET Portal/Attachment/5 : lampiran hasil pemeriksaan kesehatan MILIK karyawan yang login saja
        [HttpGet("Attachment/{id:int}")]
        [Authorize(Policy = "PortalOnly")]
        public async Task<IActionResult> Attachment(int id, bool download = false)
        {
            var employeeId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var a = await _attachmentRepository.GetAsync(id);
            if (a is null || a.EntityType != AttachmentEntityTypes.Checkup)
                return NotFound();

            var checkup = await _checkupRepository.GetByIdAsync(a.EntityId);
            if (checkup is null || checkup.EmployeeId != employeeId)
                return NotFound();   // bukan miliknya: jangan beri tahu bahwa file itu ada

            return Admin.AttachmentsController.Serve(this, _fileStorage, a, download);
        }

        [HttpPost("Logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("EmployeeLogin", "Account");
        }

        // ---------------- helper ----------------

        private async Task SignInPortalAsync(Employee employee, string role)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
                new(ClaimTypes.Name, employee.Nama),
                new("NIP", employee.NIP),
                new(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(SessionLength),
                    AllowRefresh = true
                });
        }

        // Karyawan yang login (aktif, bukan mitra), atau null
        private async Task<Employee?> CurrentEmployeeAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                return null;
            var employee = await _employeeRepository.GetByIdAsync(id);
            return employee is null || !employee.IsActive || employee.IsMitra ? null : employee;
        }

        private async Task<IActionResult> ForceLogoutAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        // Password tidak pernah dikirim balik ke browser
        private static PortalChangePasswordViewModel Cleared(PortalChangePasswordViewModel m)
        {
            m.PasswordLama = null;
            m.PasswordBaru = string.Empty;
            m.UlangiPassword = string.Empty;
            return m;
        }

        private static string NipFailKey(string nip) => $"portal-fail:nip:{nip.Trim().ToUpperInvariant()}";

        private int FailCount(string key) => _cache.TryGetValue(key, out int n) ? n : 0;

        private void AddFail(string key) =>
            _cache.Set(key, FailCount(key) + 1, FailWindow);
    }
}
