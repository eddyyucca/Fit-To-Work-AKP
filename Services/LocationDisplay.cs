using System.Globalization;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    /// <summary>Format tampilan jejak lokasi (riwayat FTW & Gym di admin, Excel).</summary>
    public static class LocationDisplay
    {
        /// <summary>"Site A · 120 m (GPS)" / "Site A (Wi-Fi)" / null kalau tidak ada data lokasi (kunci belum aktif / data lama).</summary>
        public static string? Text(ILocationStamped g)
        {
            if (g.LokasiVia is null && g.GpsLat is null)
                return null;
            var nama = g.LokasiNama ?? "Tanpa kunci lokasi";
            var jarak = g.JarakMeter is int j ? $" · {SiteGeofence.FormatDistance(j)}" : string.Empty;
            var via = g.LokasiVia switch { "WIFI" => " (Wi-Fi)", "GPS" => " (GPS)", _ => string.Empty };
            return nama + jarak + via;
        }

        /// <summary>Link Google Maps ke titik GPS saat mengisi, atau null.</summary>
        public static string? MapsUrl(ILocationStamped g) =>
            g.GpsLat is decimal lat && g.GpsLng is decimal lng
                ? string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={lat},{lng}")
                : null;
    }
}
