using System.Text;
using System.Text.Json;

namespace FitToWorkApp.Services
{
    /// <summary>Satu titik GPS dari HP: posisi (derajat), akurasi (meter), waktu (Unix ms).</summary>
    public readonly record struct TrackPoint(double Lat, double Lng, double Acc, long T);

    public class TrackResult
    {
        /// <summary>Jarak tempuh (meter, dibulatkan).</summary>
        public int DistanceMeter { get; init; }
        /// <summary>Jumlah titik yang lolos filter (dipakai menghitung jarak).</summary>
        public int PointsUsed { get; init; }
        /// <summary>Titik yang dibuang (akurasi buruk / lonjakan).</summary>
        public int PointsDropped { get; init; }
        /// <summary>Total jeda antar titik yang lebih dari <see cref="FitnessTrack.GapSeconds"/> detik (GPS sempat berhenti).</summary>
        public int GapSeconds { get; init; }
        /// <summary>Titik yang dipakai (untuk rute peta).</summary>
        public IReadOnlyList<TrackPoint> Accepted { get; init; } = Array.Empty<TrackPoint>();
    }

    /// <summary>
    /// Perhitungan jarak tempuh dari rekaman GPS (logika murni, sama persis dengan wwwroot/js/fitness-track.js
    /// supaya jarak yang tampil di HP = jarak yang tersimpan). Server SELALU menghitung ulang dari titik mentah.
    /// Aturan:
    ///  1. Titik dengan akurasi &gt; <see cref="MaxAccuracyMeter"/> m dibuang.
    ///  2. Posisi dihaluskan (rata-rata maks. 3 titik terakhir dalam 10 detik), lalu perpindahan kecil
    ///     (&lt; max(5 m, 0.75 x akurasi)) diabaikan supaya getaran GPS saat diam tidak menambah jarak.
    ///     Titik acuan tidak digeser, jadi gerakan pelan tetap terhitung setelah jaraknya cukup.
    ///  3. Lonjakan (kecepatan dari titik acuan &gt; batas kecepatan jenis aktivitas) dibuang.
    ///     Kalau <see cref="ReanchorAfter"/> titik berturut-turut dibuang, titik acuan dipindah ke titik terbaru
    ///     tanpa menambah jarak (titik acuan awal yang salah tidak mengunci rekaman selamanya).
    ///  4. Jeda antar titik berurutan &gt; <see cref="GapSeconds"/> detik dicatat sebagai celah GPS (layar terkunci /
    ///     sinyal hilang); jarak lurus di celah tetap dihitung kalau kecepatannya masuk akal.
    /// </summary>
    public static class FitnessTrack
    {
        public const double MaxAccuracyMeter = 50;
        public const double MinStepMeter = 5;
        /// <summary>Perpindahan minimal = max(5 m, 0.75 x akurasi titik).</summary>
        public const double MinStepAccuracyFactor = 0.75;
        public const int SmoothPoints = 3;
        public const long SmoothWindowMs = 10_000;
        public const int GapSeconds = 60;
        public const int ReanchorAfter = 5;
        public const double DefaultMaxSpeedKmh = 50;
        /// <summary>Batas titik yang diterima dari HP (±12 jam dengan 1 titik per 3 detik).</summary>
        public const int MaxPoints = 15000;
        /// <summary>Batas titik rute yang disimpan untuk peta (setelah disederhanakan).</summary>
        public const int MaxRoutePoints = 800;

        /// <summary>
        /// Baca JSON dari HP: [[lat, lng, akurasi, unixMs], ...]. Titik tidak valid / di luar rentang waktu sesi dibuang,
        /// hasil diurutkan menurut waktu. JSON rusak = daftar kosong (aktivitas tetap bisa diselesaikan tanpa jarak).
        /// </summary>
        public static List<TrackPoint> Parse(string? json, DateTime startUtc, DateTime endUtc)
        {
            var list = new List<TrackPoint>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            var minT = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() - 120_000;
            var maxT = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() + 120_000;

            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (list.Count >= MaxPoints)
                        break;
                    if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() < 4)
                        continue;
                    if (!TryNum(el[0], out var lat) || !TryNum(el[1], out var lng) ||
                        !TryNum(el[2], out var acc) || !TryNum(el[3], out var t))
                        continue;
                    if (lat is < -90 or > 90 || lng is < -180 or > 180 || (lat == 0 && lng == 0) || acc < 0)
                        continue;
                    var tl = (long)t;
                    if (tl < minT || tl > maxT)
                        continue;
                    list.Add(new TrackPoint(lat, lng, acc, tl));
                }
            }
            catch (JsonException)
            {
                return new List<TrackPoint>();
            }

            list.Sort((a, b) => a.T.CompareTo(b.T));
            return list;
        }

        private static bool TryNum(JsonElement el, out double value)
        {
            value = 0;
            return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value) && double.IsFinite(value);
        }

        /// <summary>Hitung jarak dari titik yang sudah urut waktu. maxSpeedKmh null = <see cref="DefaultMaxSpeedKmh"/>.</summary>
        public static TrackResult Compute(IReadOnlyList<TrackPoint> points, double? maxSpeedKmh)
        {
            var maxSpeed = maxSpeedKmh is double s && s > 0 ? s : DefaultMaxSpeedKmh;
            var accepted = new List<TrackPoint>();
            var buffer = new List<TrackPoint>(SmoothPoints + 1);
            double distance = 0;
            double gap = 0;
            int dropped = 0, rejectRun = 0;
            TrackPoint? last = null;
            long? prevT = null;

            foreach (var p in points)
            {
                if (p.Acc > MaxAccuracyMeter)
                {
                    dropped++;
                    continue;
                }

                // Celah GPS diukur antar titik berurutan (bukan antar titik acuan), supaya diam lama tidak dianggap celah
                if (prevT is long pt && (p.T - pt) / 1000d > GapSeconds)
                    gap += (p.T - pt) / 1000d;
                prevT = p.T;

                // Posisi dihaluskan: rata-rata maks. 3 titik terakhir dalam 10 detik (meredam getaran GPS)
                buffer.Add(p);
                buffer.RemoveAll(q => p.T - q.T > SmoothWindowMs);
                if (buffer.Count > SmoothPoints)
                    buffer.RemoveRange(0, buffer.Count - SmoothPoints);
                var sm = new TrackPoint(Avg(buffer, true), Avg(buffer, false), p.Acc, p.T);

                if (last is not TrackPoint a)
                {
                    last = sm;
                    accepted.Add(sm);
                    continue;
                }

                var dt = (sm.T - a.T) / 1000d;
                if (dt <= 0)
                {
                    buffer.RemoveAt(buffer.Count - 1);
                    continue;
                }

                var d = SiteGeofence.DistanceMeters(a.Lat, a.Lng, sm.Lat, sm.Lng);
                if (d < Math.Max(MinStepMeter, p.Acc * MinStepAccuracyFactor))
                {
                    rejectRun = 0;
                    continue;   // diam / getaran GPS: acuan tetap
                }

                if (d / dt * 3.6 > maxSpeed)
                {
                    dropped++;
                    buffer.RemoveAt(buffer.Count - 1);   // lonjakan tidak ikut menghaluskan titik berikutnya
                    if (++rejectRun >= ReanchorAfter)
                    {
                        // Acuan lama kemungkinan titik yang salah: mulai segmen baru dari titik ini (tanpa jarak)
                        buffer.Clear();
                        buffer.Add(p);
                        last = p;
                        accepted.Add(p);
                        rejectRun = 0;
                    }
                    continue;
                }

                rejectRun = 0;
                distance += d;
                last = sm;
                accepted.Add(sm);
            }

            return new TrackResult
            {
                DistanceMeter = (int)Math.Round(distance, MidpointRounding.AwayFromZero),
                PointsUsed = accepted.Count,
                PointsDropped = dropped,
                GapSeconds = (int)Math.Round(gap, MidpointRounding.AwayFromZero),
                Accepted = accepted
            };
        }

        // Rata-rata berurutan (urutan penjumlahan sama dengan fitness-track.js)
        private static double Avg(List<TrackPoint> list, bool lat)
        {
            double sum = 0;
            foreach (var q in list)
                sum += lat ? q.Lat : q.Lng;
            return sum / list.Count;
        }

        // ---------------- Rute untuk peta ----------------

        /// <summary>Sederhanakan rute (Ramer-Douglas-Peucker, toleransi meter) lalu batasi jumlah titik.</summary>
        public static List<TrackPoint> Simplify(IReadOnlyList<TrackPoint> points, double toleranceMeter = 4, int maxPoints = MaxRoutePoints)
        {
            if (points.Count <= 2)
                return points.ToList();

            var keep = new bool[points.Count];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int Start, int End)>();
            stack.Push((0, points.Count - 1));
            var lat0 = points[0].Lat * Math.PI / 180;

            while (stack.Count > 0)
            {
                var (start, end) = stack.Pop();
                double maxDist = 0;
                var index = -1;
                for (var i = start + 1; i < end; i++)
                {
                    var dist = PerpendicularMeters(points[i], points[start], points[end], lat0);
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        index = i;
                    }
                }
                if (index > 0 && maxDist > toleranceMeter)
                {
                    keep[index] = true;
                    stack.Push((start, index));
                    stack.Push((index, end));
                }
            }

            var result = points.Where((_, i) => keep[i]).ToList();
            if (result.Count <= maxPoints)
                return result;

            // Masih terlalu banyak: ambil merata, titik awal & akhir selalu ikut
            var step = (result.Count - 1) / (double)(maxPoints - 1);
            return Enumerable.Range(0, maxPoints).Select(i => result[(int)Math.Round(i * step)]).ToList();
        }

        // Jarak titik ke garis (meter) dengan proyeksi equirectangular (cukup akurat untuk jarak pendek)
        private static double PerpendicularMeters(TrackPoint p, TrackPoint a, TrackPoint b, double lat0)
        {
            const double r = 6_371_000d;
            static double Rad(double deg) => deg * Math.PI / 180d;
            double X(TrackPoint q) => Rad(q.Lng) * Math.Cos(lat0) * r;
            double Y(TrackPoint q) => Rad(q.Lat) * r;

            double ax = X(a), ay = Y(a), bx = X(b), by = Y(b), px = X(p), py = Y(p);
            double dx = bx - ax, dy = by - ay;
            var len2 = dx * dx + dy * dy;
            if (len2 == 0)
                return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
            double cx = ax + t * dx, cy = ay + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        /// <summary>Encoded polyline (format Google, presisi 5 desimal). Didekode di wwwroot/js/fitness-track.js.</summary>
        public static string EncodePolyline(IEnumerable<TrackPoint> points)
        {
            var sb = new StringBuilder();
            long prevLat = 0, prevLng = 0;
            foreach (var p in points)
            {
                var lat = (long)Math.Round(p.Lat * 1e5);
                var lng = (long)Math.Round(p.Lng * 1e5);
                Encode(lat - prevLat, sb);
                Encode(lng - prevLng, sb);
                prevLat = lat;
                prevLng = lng;
            }
            return sb.ToString();

            static void Encode(long v, StringBuilder sb)
            {
                v = v < 0 ? ~(v << 1) : v << 1;
                while (v >= 0x20)
                {
                    sb.Append((char)((0x20 | (v & 0x1f)) + 63));
                    v >>= 5;
                }
                sb.Append((char)(v + 63));
            }
        }

        public static List<(double Lat, double Lng)> DecodePolyline(string? encoded)
        {
            var result = new List<(double, double)>();
            if (string.IsNullOrEmpty(encoded))
                return result;
            int index = 0;
            long lat = 0, lng = 0;
            while (index < encoded.Length)
            {
                lat += Next(encoded, ref index);
                if (index >= encoded.Length)
                    break;
                lng += Next(encoded, ref index);
                result.Add((lat / 1e5, lng / 1e5));
            }
            return result;

            static long Next(string s, ref int i)
            {
                long res = 0;
                var shift = 0;
                int b;
                do
                {
                    if (i >= s.Length)
                        return 0;
                    b = s[i++] - 63;
                    res |= (long)(b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                return (res & 1) != 0 ? ~(res >> 1) : res >> 1;
            }
        }
    }
}
