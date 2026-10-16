using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Services
{
    /// <summary>
    /// NIP diingat di HP: karyawan cukup sekali memasukkan NIP di HP masing-masing (tidak ada kios bersama).
    ///
    /// Cara kerja:
    ///  - Setiap login NIP berhasil, server menitipkan cookie terpisah <see cref="CookieName"/>
    ///    berisi ID karyawan yang DIENKRIPSI + diberi tanda tangan (ASP.NET Data Protection), berlaku 90 hari.
    ///    Isinya tidak bisa dibaca atau diubah dari HP (mis. diganti ID karyawan lain).
    ///  - Cookie login biasa (FitToWork.Auth) tetap cookie sesi. Kalau browser ditutup lalu dibuka lagi,
    ///    halaman login membaca cookie "ingat saya" dan langsung memasukkan karyawan ke Menu.
    ///  - Cookie ini sengaja DIPISAH dari cookie login, karena "Riwayat Saya" (NIP + tanggal lahir) dan login admin
    ///    memakai cookie login yang sama. Jadi masuk/keluar Riwayat Saya tidak menghapus "ingat saya".
    ///  - Setiap kali dipakai, masa berlaku diperpanjang lagi 90 hari (selama HP rutin dipakai, tidak perlu input NIP lagi).
    ///  - Dicabut otomatis kalau karyawan dinonaktifkan / dipindah jadi mitra, atau karyawan menekan "Bukan Anda? Keluar".
    /// </summary>
    public interface IEmployeeDeviceLogin
    {
        /// <summary>Simpan cookie "ingat saya" untuk karyawan ini di browser/HP yang sedang dipakai (90 hari).</summary>
        void Remember(HttpContext http, int employeeId);

        /// <summary>ID karyawan dari cookie "ingat saya", atau null kalau tidak ada / rusak / kedaluwarsa.</summary>
        int? Read(HttpContext http);

        /// <summary>Hapus cookie "ingat saya" dari browser ini.</summary>
        void Forget(HttpContext http);

        /// <summary>Login karyawan (cookie FitToWork.Auth).</summary>
        Task SignInAsync(HttpContext http, Employee employee);

        /// <summary>
        /// Karyawan masih boleh login? (ada, aktif, bukan mitra). Hasil di-cache 60 detik supaya tidak query
        /// database di setiap request; dipakai juga oleh cookie validation di Program.cs.
        /// </summary>
        Task<bool> IsAllowedAsync(int employeeId);
    }

    public class EmployeeDeviceLogin : IEmployeeDeviceLogin
    {
        public const string CookieName = "AKPSehat.Device";
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);
        private static readonly TimeSpan AllowedCacheLength = TimeSpan.FromSeconds(60);

        private readonly ITimeLimitedDataProtector _protector;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IMemoryCache _cache;

        public EmployeeDeviceLogin(IDataProtectionProvider dataProtection, IEmployeeRepository employeeRepository, IMemoryCache cache)
        {
            // "Purpose" khusus fitur ini: token ini tidak bisa dipakai ulang sebagai cookie lain, dan sebaliknya.
            _protector = dataProtection.CreateProtector("FitToWork.EmployeeDeviceLogin.v1").ToTimeLimitedDataProtector();
            _employeeRepository = employeeRepository;
            _cache = cache;
        }

        public void Remember(HttpContext http, int employeeId)
        {
            var token = _protector.Protect(employeeId.ToString(), Lifetime);
            http.Response.Cookies.Append(CookieName, token, BuildOptions(http, DateTimeOffset.UtcNow.Add(Lifetime)));
        }

        public int? Read(HttpContext http)
        {
            if (!http.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
                return null;
            try
            {
                // Unprotect gagal (exception) kalau token diubah, dibuat di server lain, atau lewat 90 hari
                var raw = _protector.Unprotect(token);
                return int.TryParse(raw, out var id) ? id : null;
            }
            catch
            {
                return null;
            }
        }

        public void Forget(HttpContext http)
        {
            if (http.Request.Cookies.ContainsKey(CookieName))
                http.Response.Cookies.Delete(CookieName, BuildOptions(http, null));
        }

        public async Task SignInAsync(HttpContext http, Employee employee)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
                new(ClaimTypes.Name, employee.Nama),
                new("NIP", employee.NIP),
                new("Section", employee.SectionName ?? string.Empty),
                new(ClaimTypes.Role, "Employee")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        public async Task<bool> IsAllowedAsync(int employeeId)
        {
            var key = $"emp-allowed:{employeeId}";
            if (_cache.TryGetValue(key, out bool allowed))
                return allowed;

            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            allowed = employee is not null && employee.IsActive && !employee.IsMitra;
            _cache.Set(key, allowed, AllowedCacheLength);
            return allowed;
        }

        private static CookieOptions BuildOptions(HttpContext http, DateTimeOffset? expires) => new()
        {
            HttpOnly = true,                         // tidak bisa dibaca JavaScript
            Secure = http.Request.IsHttps,           // lewat Cloudflare = HTTPS (lihat UseForwardedHeaders)
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            Expires = expires
        };
    }
}
