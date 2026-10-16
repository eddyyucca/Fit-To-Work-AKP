using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    /// <summary>
    /// Cek lokasi lebih awal dari halaman karyawan (sebelum form dikirim), supaya karyawan langsung tahu
    /// apakah posisinya sudah di area site. Penentu akhirnya tetap pengecekan ulang di POST FTW / Gym.
    /// </summary>
    [Authorize(Policy = "EmployeeOnly")]
    public class LocationController : Controller
    {
        private readonly ILocationGuard _guard;

        public LocationController(ILocationGuard guard)
        {
            _guard = guard;
        }

        // GET /Location/Check?lat=-0.5021&lng=117.1536&acc=15
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Check(string? lat, string? lng, string? acc)
        {
            var r = await _guard.CheckAsync(HttpContext, lat, lng, acc);
            return Json(new
            {
                enforced = r.Enforced,
                allowed = r.Allowed,
                via = r.Via,
                site = r.Site?.Nama,
                jarak = r.DistanceMeter,
                message = r.Message
            });
        }
    }
}
