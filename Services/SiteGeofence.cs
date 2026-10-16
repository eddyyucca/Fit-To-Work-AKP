using System.Globalization;
using System.Net;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    /// <summary>Posisi HP yang dikirim browser (derajat desimal, akurasi dalam meter).</summary>
    public record GeoInput(double Lat, double Lng, double? Accuracy);

    public class GeofenceResult
    {
        /// <summary>false = belum ada lokasi site aktif, jadi kunci lokasi tidak berlaku.</summary>
        public bool Enforced { get; init; }
        public bool Allowed { get; init; }
        /// <summary>Site yang cocok (atau site terdekat kalau ditolak).</summary>
        public SiteLocation? Site { get; init; }
        public int? DistanceMeter { get; init; }
        /// <summary>'GPS' | 'WIFI' | null</summary>
        public string? Via { get; init; }
        public GeoInput? Input { get; init; }
        public string Message { get; init; } = string.Empty;

        /// <summary>Salin hasil pengecekan ke kolom jejak lokasi (FTW / Gym).</summary>
        public void StampTo(ILocationStamped target)
        {
            target.LokasiSiteId = Allowed && Enforced ? Site?.Id : null;
            target.LokasiNama = Allowed && Enforced ? Site?.Nama : null;
            target.GpsLat = Input is null ? null : Math.Round((decimal)Input.Lat, 6);
            target.GpsLng = Input is null ? null : Math.Round((decimal)Input.Lng, 6);
            target.GpsAkurasi = Input?.Accuracy is double a ? (int)Math.Min(Math.Round(a), int.MaxValue) : null;
            target.JarakMeter = DistanceMeter;
            target.LokasiVia = Enforced ? Via : null;
        }
    }

    /// <summary>
    /// Logika murni kunci lokasi (tanpa database / HTTP) supaya mudah di-unit-test.
    /// Aturan:
    ///  1. Tidak ada lokasi site aktif      -> tidak dikunci (boleh dari mana saja).
    ///  2. IP publik cocok dengan Wi-Fi site -> boleh (via WIFI), walau GPS lemah/tidak ada.
    ///  3. GPS tidak ada                     -> ditolak.
    ///  4. Jarak ke site terdekat &lt;= radius dan akurasi &lt;= <see cref="MaxAkurasiMeter"/> -> boleh (via GPS).
    ///  5. Selain itu ditolak, dengan pesan jarak / akurasi.
    /// </summary>
    public static class SiteGeofence
    {
        /// <summary>Akurasi GPS terburuk yang masih diterima (meter).</summary>
        public const int MaxAkurasiMeter = 200;

        private const double EarthRadiusMeter = 6_371_000d;

        /// <summary>Jarak dua titik di permukaan bumi (rumus Haversine), dalam meter.</summary>
        public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            static double Rad(double deg) => deg * Math.PI / 180d;
            var dLat = Rad(lat2 - lat1);
            var dLng = Rad(lng2 - lng1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * EarthRadiusMeter * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        /// <summary>Baca lat/lng/akurasi dari form (format angka titik, mis. "-0.502106"). null kalau tidak valid.</summary>
        public static GeoInput? Parse(string? lat, string? lng, string? accuracy)
        {
            const NumberStyles style = NumberStyles.Float;
            var ci = CultureInfo.InvariantCulture;
            if (!double.TryParse(lat, style, ci, out var la) || !double.TryParse(lng, style, ci, out var lo))
                return null;
            if (double.IsNaN(la) || double.IsNaN(lo) || la is < -90 or > 90 || lo is < -180 or > 180)
                return null;
            // 0,0 = nilai kosong dari perangkat yang gagal membaca lokasi
            if (la == 0 && lo == 0)
                return null;

            double? acc = double.TryParse(accuracy, style, ci, out var a) && a >= 0 && !double.IsNaN(a) ? a : null;
            return new GeoInput(la, lo, acc);
        }

        public static bool IpMatches(string? clientIp, SiteLocation site)
        {
            if (string.IsNullOrWhiteSpace(clientIp) || !IPAddress.TryParse(clientIp.Trim(), out var client))
                return false;
            if (client.IsIPv4MappedToIPv6)
                client = client.MapToIPv4();

            foreach (var entry in site.IpList)
            {
                if (IPAddress.TryParse(entry, out var ip))
                {
                    if (ip.IsIPv4MappedToIPv6)
                        ip = ip.MapToIPv4();
                    if (ip.Equals(client))
                        return true;
                }
            }
            return false;
        }

        public static GeofenceResult Evaluate(IReadOnlyList<SiteLocation> activeSites, GeoInput? gps, string? clientIp)
        {
            if (activeSites.Count == 0)
                return new GeofenceResult { Enforced = false, Allowed = true, Input = gps, Message = "Kunci lokasi belum diaktifkan." };

            // Site terdekat dari posisi GPS
            SiteLocation? nearest = null;
            double nearestDistance = double.MaxValue;
            if (gps is not null)
            {
                foreach (var s in activeSites)
                {
                    var d = DistanceMeters(gps.Lat, gps.Lng, (double)s.Latitude, (double)s.Longitude);
                    // Yang dicari: site yang posisinya paling "masuk" radius (jarak dikurangi radius terkecil)
                    if (nearest is null || d - s.RadiusMeter < nearestDistance - nearest.RadiusMeter)
                    {
                        nearest = s;
                        nearestDistance = d;
                    }
                }
            }

            // Wi-Fi site: IP publik cocok -> boleh
            var wifiSite = activeSites.FirstOrDefault(s => IpMatches(clientIp, s));
            if (wifiSite is not null)
            {
                int? jarak = gps is null ? null
                    : (int)Math.Round(DistanceMeters(gps.Lat, gps.Lng, (double)wifiSite.Latitude, (double)wifiSite.Longitude));
                return new GeofenceResult
                {
                    Enforced = true, Allowed = true, Site = wifiSite, DistanceMeter = jarak, Via = "WIFI", Input = gps,
                    Message = $"Terhubung ke Wi-Fi {wifiSite.Nama}."
                };
            }

            if (gps is null || nearest is null)
            {
                return new GeofenceResult
                {
                    Enforced = true, Allowed = false, Input = gps,
                    Message = "Lokasi HP belum terbaca. Aktifkan GPS dan izinkan akses lokasi, lalu coba lagi."
                };
            }

            var distance = (int)Math.Round(nearestDistance);
            var accuracy = gps.Accuracy ?? 0;

            if (distance <= nearest.RadiusMeter && accuracy <= MaxAkurasiMeter)
            {
                return new GeofenceResult
                {
                    Enforced = true, Allowed = true, Site = nearest, DistanceMeter = distance, Via = "GPS", Input = gps,
                    Message = $"Anda berada di area {nearest.Nama} (±{FormatDistance(distance)} dari titik site)."
                };
            }

            // Masih mungkin di dalam radius, tapi sinyal GPS terlalu kasar untuk dipastikan
            if (accuracy > MaxAkurasiMeter && distance - accuracy <= nearest.RadiusMeter)
            {
                return new GeofenceResult
                {
                    Enforced = true, Allowed = false, Site = nearest, DistanceMeter = distance, Via = "GPS", Input = gps,
                    Message = $"Sinyal lokasi kurang akurat (±{FormatDistance(accuracy)}). Aktifkan GPS mode akurasi tinggi, " +
                              "pindah ke tempat terbuka atau sambungkan ke Wi-Fi site, lalu coba lagi."
                };
            }

            return new GeofenceResult
            {
                Enforced = true, Allowed = false, Site = nearest, DistanceMeter = distance, Via = "GPS", Input = gps,
                Message = $"Anda berada ±{FormatDistance(distance)} dari {nearest.Nama} " +
                          $"(batas {FormatDistance(nearest.RadiusMeter)}). Pengisian hanya bisa dilakukan di area site."
            };
        }

        /// <summary>850 -> "850 m", 12345 -> "12,3 km" (format Indonesia).</summary>
        public static string FormatDistance(double meter)
        {
            if (meter < 1000)
                return $"{Math.Round(meter):0} m";
            var km = Math.Round(meter / 1000d, meter < 100_000 ? 1 : 0);
            return km.ToString(meter < 100_000 ? "0.0" : "#,0", CultureInfo.InvariantCulture)
                     .Replace(",", " ").Replace(".", ",").Replace(" ", ".") + " km";
        }
    }
}
