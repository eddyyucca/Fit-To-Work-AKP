using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        // Pembatas tebak password admin: 5x salah per username atau 20x per IP dalam 15 menit -> dikunci sementara
        private const int MaxAdminFailPerUser = 5;
        private const int MaxAdminFailPerIp = 20;
        private static readonly TimeSpan AdminFailWindow = TimeSpan.FromMinutes(15);

        // Sesi admin (akses data medis): habis setelah 2 jam tidak ada aktivitas
        private static readonly TimeSpan AdminSessionLength = TimeSpan.FromHours(2);

        // Dipakai saat username tidak ada, supaya waktu respon sama (tidak bisa menebak username dari lamanya proses)
        private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString()));

        private readonly IEmployeeRepository _employeeRepository;
        private readonly IAdminUserRepository _adminUserRepository;
        private readonly IAccessLogger _accessLogger;
        private readonly IMemoryCache _cache;
        private readonly IEmployeeDeviceLogin _deviceLogin;

        public AccountController(IEmployeeRepository employeeRepository, IAdminUserRepository adminUserRepository,
            IAccessLogger accessLogger, IMemoryCache cache, IEmployeeDeviceLogin deviceLogin)
        {
            _deviceLogin = deviceLogin;
            _employeeRepository = employeeRepository;
            _adminUserRepository = adminUserRepository;
            _accessLogger = accessLogger;
            _cache = cache;
        }

        // ---------------- Login Karyawan (halaman publik) ----------------

        [HttpGet]
        public async Task<IActionResult> EmployeeLogin(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Employee"))
                return RedirectToAction("Index", "Menu");

            // NIP diingat di HP ini (pernah login sebelumnya) -> langsung masuk tanpa input NIP lagi.
            // Admin yang sedang login di browser yang sama tidak ditimpa sesinya.
            if (!User.IsInRole("Admin") && _deviceLogin.Read(HttpContext) is int rememberedId)
            {
                var employee = await _employeeRepository.GetByIdAsync(rememberedId);
                if (employee is not null && employee.IsActive && !employee.IsMitra)
                {
                    await _deviceLogin.SignInAsync(HttpContext, employee);
                    _deviceLogin.Remember(HttpContext, employee.Id);   // perpanjang lagi 90 hari
                    return RedirectAfterEmployeeLogin(returnUrl);
                }

                // Karyawan sudah nonaktif / jadi mitra / dihapus: cabut, tampilkan layar NIP biasa
                _deviceLogin.Forget(HttpContext);
            }

            return View(new EmployeeLoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeLogin(EmployeeLoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var employee = await _employeeRepository.GetByNIPAsync(model.NIP.Trim());
            // Karyawan mitra hanya punya riwayat kunjungan klinik, tidak mengisi Fit To Work / Daftar Gym
            if (employee is null || !employee.IsActive || employee.IsMitra)
            {
                ModelState.AddModelError(string.Empty, "NIP tidak ditemukan atau akun tidak aktif. Hubungi admin/HR jika perlu bantuan.");
                return View(model);
            }

            // Karyawan login dari HP masing-masing: NIP langsung diingat di HP ini (90 hari, diperpanjang tiap dipakai)
            await _deviceLogin.SignInAsync(HttpContext, employee);
            _deviceLogin.Remember(HttpContext, employee.Id);

            return RedirectAfterEmployeeLogin(model.ReturnUrl);
        }

        private IActionResult RedirectAfterEmployeeLogin(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Menu");
        }

        // ---------------- Login Admin ----------------

        [HttpGet]
        public IActionResult AdminLogin(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
                return RedirectToAction("Home", "Admin");

            return View(new AdminLoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminLogin(AdminLoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var username = model.Username.Trim();
            var ip = AccessLogger.GetClientIp(HttpContext) ?? "unknown";
            var userKey = $"admin-fail:user:{username.ToUpperInvariant()}";
            var ipKey = $"admin-fail:ip:{ip}";

            if (FailCount(userKey) >= MaxAdminFailPerUser || FailCount(ipKey) >= MaxAdminFailPerIp)
            {
                await _accessLogger.LogAsync(AccessLogEvents.LoginFailed, "Login diblokir sementara: terlalu banyak percobaan gagal",
                    StatusCodes.Status429TooManyRequests, username: username);
                ModelState.AddModelError(string.Empty, "Terlalu banyak percobaan login gagal. Coba lagi 15 menit lagi atau hubungi Super Admin.");
                return View(model);
            }

            var admin = await _adminUserRepository.GetByUsernameAsync(username);
            var passwordOk = PasswordHasher.Verify(model.Password, admin?.PasswordHash ?? DummyHash.Value);
            if (admin is null || !passwordOk)
            {
                AddFail(userKey);
                AddFail(ipKey);

                // Username yang dicoba ikut dicatat (untuk deteksi tebak password); password TIDAK pernah dicatat
                await _accessLogger.LogAsync(AccessLogEvents.LoginFailed,
                    admin is null ? "Login gagal: username tidak ditemukan" : "Login gagal: password salah",
                    StatusCodes.Status200OK, username: username, adminUserId: admin?.Id);

                ModelState.AddModelError(string.Empty, "Username atau password salah.");
                return View(model);
            }

            _cache.Remove(userKey);

            // Password benar tapi akun dinonaktifkan: tolak dengan pesan jelas (tidak dihitung sebagai tebakan password)
            if (!admin.IsActive)
            {
                await _accessLogger.LogAsync(AccessLogEvents.LoginFailed, "Login ditolak: akun nonaktif",
                    StatusCodes.Status200OK, username: username, adminUserId: admin.Id);
                ModelState.AddModelError(string.Empty, "Akun Anda sedang dinonaktifkan. Hubungi Super Admin.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new(ClaimTypes.Name, admin.FullName),
                new("Username", admin.Username),
                new(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = false,                       // cookie hilang saat browser ditutup
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(AdminSessionLength),
                    AllowRefresh = true                         // diperpanjang selama masih aktif dipakai
                });

            await _accessLogger.LogAsync(AccessLogEvents.Login, "Login admin berhasil", StatusCodes.Status302Found,
                username: admin.Username, adminUserId: admin.Id);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Home", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var isAdmin = User.IsInRole("Admin");
            if (isAdmin)
                await _accessLogger.LogAsync(AccessLogEvents.Logout, "Logout admin", StatusCodes.Status302Found);
            // Karyawan menekan "Keluar" / "Bukan Anda? Keluar": HP ini tidak diingat lagi.
            // (Logout admin tidak menyentuh "ingat saya" karyawan di browser yang sama.)
            if (User.IsInRole("Employee"))
                _deviceLogin.Forget(HttpContext);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(isAdmin ? "AdminLogin" : "EmployeeLogin");
        }

        // Akses ditolak dicatat di server (AccessLogAuthorizationResultHandler & AccessLogMiddleware),
        // bukan di sini, supaya tetap tercatat walau browser tidak mengikuti redirect.
        [HttpGet]
        public IActionResult AccessDenied() => View();

        private int FailCount(string key) => _cache.TryGetValue(key, out int n) ? n : 0;

        private void AddFail(string key) => _cache.Set(key, FailCount(key) + 1, AdminFailWindow);
    }
}
