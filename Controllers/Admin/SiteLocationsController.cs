using System.ComponentModel.DataAnnotations;
using System.Net;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class SiteLocationFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama lokasi wajib diisi")]
        [StringLength(100, ErrorMessage = "Nama maksimal 100 karakter")]
        [Display(Name = "Nama Lokasi")]
        public string Nama { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tentukan titik lokasi di peta")]
        [Range(-90, 90, ErrorMessage = "Latitude harus antara -90 dan 90")]
        public decimal? Latitude { get; set; }

        [Required(ErrorMessage = "Tentukan titik lokasi di peta")]
        [Range(-180, 180, ErrorMessage = "Longitude harus antara -180 dan 180")]
        public decimal? Longitude { get; set; }

        [Required(ErrorMessage = "Radius wajib diisi")]
        [Range(SiteLocation.MinRadius, SiteLocation.MaxRadius, ErrorMessage = "Radius harus antara 50 dan 20.000 meter")]
        [Display(Name = "Radius (meter)")]
        public int? RadiusMeter { get; set; } = 500;

        [StringLength(300, ErrorMessage = "IP publik maksimal 300 karakter")]
        [Display(Name = "IP Publik Wi-Fi Site (opsional)")]
        public string? IpPublik { get; set; }

        [StringLength(250, ErrorMessage = "Keterangan maksimal 250 karakter")]
        public string? Keterangan { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master Lokasi Site untuk kunci lokasi pengisian Fit To Work & Gym. Titik & radius dipilih lewat peta
    /// (Leaflet + OpenStreetMap / citra satelit). Kalau tidak ada lokasi aktif, kunci lokasi tidak berlaku.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MasterLocations)]
    [Route("Admin/SiteLocations")]
    public class SiteLocationsController : Controller
    {
        private readonly ISiteLocationRepository _repo;
        private readonly ILocationGuard _guard;

        public SiteLocationsController(ISiteLocationRepository repo, ILocationGuard guard)
        {
            _repo = repo;
            _guard = guard;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewBag.MyIp = AccessLogger.GetClientIp(HttpContext);
            return View(await _repo.GetAllAsync());
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            await FillEditViewBagAsync(0);
            return View("Edit", new SiteLocationFormViewModel());
        }

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _repo.GetByIdAsync(id);
            if (item is null)
                return NotFound();

            await FillEditViewBagAsync(id);
            return View(new SiteLocationFormViewModel
            {
                Id = item.Id,
                Nama = item.Nama,
                Latitude = item.Latitude,
                Longitude = item.Longitude,
                RadiusMeter = item.RadiusMeter,
                IpPublik = item.IpPublik,
                Keterangan = item.Keterangan,
                IsActive = item.IsActive
            });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(SiteLocationFormViewModel model)
        {
            model.Nama = (model.Nama ?? string.Empty).Trim();
            model.Keterangan = string.IsNullOrWhiteSpace(model.Keterangan) ? null : model.Keterangan.Trim();
            model.IpPublik = NormalizeIps(model.IpPublik, out var invalidIp);
            if (invalidIp is not null)
                ModelState.AddModelError(nameof(model.IpPublik), $"'{invalidIp}' bukan alamat IP yang valid.");

            if (model.Id > 0 && await _repo.GetByIdAsync(model.Id) is null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                await FillEditViewBagAsync(model.Id);
                return View("Edit", model);
            }

            await _repo.SaveAsync(new SiteLocation
            {
                Id = model.Id,
                Nama = model.Nama,
                Latitude = Math.Round(model.Latitude!.Value, 6),
                Longitude = Math.Round(model.Longitude!.Value, 6),
                RadiusMeter = model.RadiusMeter!.Value,
                IpPublik = model.IpPublik,
                Keterangan = model.Keterangan,
                IsActive = model.IsActive
            });
            _guard.InvalidateCache();   // langsung berlaku untuk karyawan (tanpa menunggu cache 60 detik)

            TempData["Success"] = $"Lokasi {model.Nama} berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var item = await _repo.GetByIdAsync(id);
            if (item is null)
                return NotFound();

            await _repo.SetActiveAsync(id, activate);
            _guard.InvalidateCache();
            TempData["Success"] = $"Lokasi {item.Nama} {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _repo.GetByIdAsync(id);
            if (item is null)
                return NotFound();

            await _repo.DeleteAsync(id);
            _guard.InvalidateCache();
            TempData["Success"] = $"Lokasi {item.Nama} dihapus. Riwayat pengisian yang lama tetap menyimpan nama lokasinya.";
            return RedirectToAction(nameof(Index));
        }

        // Lokasi lain ditampilkan samar di peta form, supaya radius yang tumpang tindih kelihatan
        private async Task FillEditViewBagAsync(int currentId)
        {
            ViewBag.MyIp = AccessLogger.GetClientIp(HttpContext);
            ViewBag.OtherSites = (await _repo.GetAllAsync())
                .Where(s => s.Id != currentId)
                .Select(s => new { nama = s.Nama, lat = s.Latitude, lng = s.Longitude, radius = s.RadiusMeter, aktif = s.IsActive })
                .ToList();
        }

        /// <summary>Rapikan daftar IP (dipisah koma), buang duplikat. <paramref name="invalid"/> = entri pertama yang tidak valid.</summary>
        private static string? NormalizeIps(string? raw, out string? invalid)
        {
            invalid = null;
            var list = new SiteLocation { IpPublik = raw }.IpList;
            var result = new List<string>();
            foreach (var entry in list)
            {
                if (!IPAddress.TryParse(entry, out var ip))
                {
                    invalid = entry;
                    return raw;
                }
                var text = ip.ToString();
                if (!result.Contains(text))
                    result.Add(text);
            }
            return result.Count == 0 ? null : string.Join(", ", result);
        }
    }
}
