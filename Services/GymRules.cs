using System.Globalization;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    /// <summary>
    /// Aturan sesi aktivitas kebugaran (logika murni, mudah di-unit-test):
    ///  - Mulai aktivitas baru hanya boleh kalau tidak ada sesi yang sedang berlangsung (selesaikan dulu).
    ///  - Sesi yang tidak diselesaikan sampai <see cref="GymAttendance.BatasAt"/> (mulai + batas jam jenis aktivitas)
    ///    otomatis ditutup "Tidak diselesaikan" (durasi & jarak tidak dihitung), lalu karyawan boleh mulai lagi.
    ///  - Boleh lebih dari 1 sesi per hari.
    /// (Nama kelas tetap GymRules karena fitur ini berawal dari gym.)
    /// </summary>
    public static class GymRules
    {
        /// <summary>Batas jam untuk data lama yang belum punya <see cref="GymAttendance.BatasAt"/>.</summary>
        public const int MaxSessionHours = 3;

        public enum SessionState { InGym, Finished, NoCheckOut, Legacy }

        /// <summary>Batas sesi (UTC). Data lama tanpa BatasAt = mulai + 3 jam.</summary>
        public static DateTime DeadlineUtc(GymAttendance g) => g.BatasAt ?? g.CreatedAt.AddHours(MaxSessionHours);

        public static SessionState GetState(GymAttendance g, DateTime nowUtc) => g.CheckOutStatus switch
        {
            GymCheckOutStatus.Manual => SessionState.Finished,
            GymCheckOutStatus.Auto => SessionState.NoCheckOut,
            GymCheckOutStatus.Legacy => SessionState.Legacy,
            // Belum ditutup background service tapi sudah lewat batas -> dianggap tidak diselesaikan
            _ => DeadlineUtc(g) > nowUtc ? SessionState.InGym : SessionState.NoCheckOut
        };

        public static bool IsOpen(GymAttendance g, DateTime nowUtc) => GetState(g, nowUtc) == SessionState.InGym;

        public static string StatusLabel(SessionState s) => s switch
        {
            SessionState.InGym => "Berlangsung",
            SessionState.Finished => "Selesai",
            SessionState.NoCheckOut => "Tidak diselesaikan",
            _ => "Data lama"
        };

        /// <summary>Kelas badge Bootstrap untuk status.</summary>
        public static string StatusBadge(SessionState s) => s switch
        {
            SessionState.InGym => "text-bg-success",
            SessionState.Finished => "text-bg-primary",
            SessionState.NoCheckOut => "text-bg-warning",
            _ => "text-bg-secondary"
        };

        /// <summary>65 -> "1 jam 5 mnt", 40 -> "40 mnt".</summary>
        public static string FormatDuration(int? minutes)
        {
            if (minutes is null)
                return "-";
            var m = Math.Max(0, minutes.Value);
            if (m < 60)
                return $"{m} mnt";
            return m % 60 == 0 ? $"{m / 60} jam" : $"{m / 60} jam {m % 60} mnt";
        }

        /// <summary>Jarak tempuh: 850 -> "850 m", 5230 -> "5.23 km". null -> "-".</summary>
        public static string FormatDistance(int? meter)
        {
            if (meter is null)
                return "-";
            var m = Math.Max(0, meter.Value);
            return m < 1000
                ? $"{m} m"
                : (m / 1000d).ToString("0.00", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>
        /// Pace (menit per km), mis. "6:05 /km". null kalau jarak &lt; 100 m atau durasi kosong
        /// (pace dari jarak sangat pendek tidak bermakna).
        /// </summary>
        public static string? FormatPace(int? durasiDetik, int? meter)
        {
            if (durasiDetik is not int d || meter is not int m || m < 100 || d <= 0)
                return null;
            var detikPerKm = (int)Math.Round(d / (m / 1000d));
            if (detikPerKm > 99 * 60)
                return null;
            return $"{detikPerKm / 60}:{detikPerKm % 60:00} /km";
        }

        /// <summary>Kecepatan rata-rata, mis. "18.4 km/j" (dipakai untuk sepeda).</summary>
        public static string? FormatSpeed(int? durasiDetik, int? meter)
        {
            if (durasiDetik is not int d || meter is not int m || m < 100 || d <= 0)
                return null;
            return (m / 1000d / (d / 3600d)).ToString("0.0", CultureInfo.InvariantCulture) + " km/j";
        }
    }
}
