using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Services
{
    /// <summary>
    /// Pengecekan kunci lokasi untuk halaman karyawan (FTW & Gym).
    /// Daftar lokasi aktif di-cache 60 detik; admin yang mengubah lokasi memanggil <see cref="InvalidateCache"/>.
    /// Server SELALU mengecek ulang saat POST, jadi JavaScript di browser hanya untuk kenyamanan (info lebih awal).
    /// </summary>
    public interface ILocationGuard
    {
        Task<GeofenceResult> CheckAsync(HttpContext http, string? lat, string? lng, string? accuracy);

        /// <summary>Info untuk tampilan form: kunci aktif? sudah terhubung Wi-Fi site?</summary>
        Task<LocationUiModel> GetUiAsync(HttpContext http);

        void InvalidateCache();
    }

    public class LocationGuard : ILocationGuard
    {
        private const string CacheKey = "site-locations:active";

        private readonly ISiteLocationRepository _repo;
        private readonly IMemoryCache _cache;

        public LocationGuard(ISiteLocationRepository repo, IMemoryCache cache)
        {
            _repo = repo;
            _cache = cache;
        }

        private async Task<IReadOnlyList<SiteLocation>> GetActiveAsync()
        {
            if (_cache.TryGetValue(CacheKey, out IReadOnlyList<SiteLocation>? cached) && cached is not null)
                return cached;

            var list = await _repo.GetAllAsync(onlyActive: true);
            _cache.Set(CacheKey, (IReadOnlyList<SiteLocation>)list, TimeSpan.FromSeconds(60));
            return list;
        }

        public async Task<GeofenceResult> CheckAsync(HttpContext http, string? lat, string? lng, string? accuracy)
        {
            var sites = await GetActiveAsync();
            return SiteGeofence.Evaluate(sites, SiteGeofence.Parse(lat, lng, accuracy), AccessLogger.GetClientIp(http));
        }

        public async Task<LocationUiModel> GetUiAsync(HttpContext http)
        {
            var sites = await GetActiveAsync();
            if (sites.Count == 0)
                return new LocationUiModel { Mode = LocationUiModel.ModeOff };

            var ip = AccessLogger.GetClientIp(http);
            var wifi = sites.FirstOrDefault(s => SiteGeofence.IpMatches(ip, s));
            return wifi is null
                ? new LocationUiModel { Mode = LocationUiModel.ModeGps }
                : new LocationUiModel { Mode = LocationUiModel.ModeWifi, WifiSiteName = wifi.Nama };
        }

        public void InvalidateCache() => _cache.Remove(CacheKey);
    }
}
