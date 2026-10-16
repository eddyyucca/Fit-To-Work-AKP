namespace FitToWorkApp.Services
{
    /// <summary>Warna achievement pengisian FTW per section.</summary>
    public enum AchievementLevel
    {
        Hijau,   // >= 90%
        Kuning,  // 70% - 89,9%
        Merah    // < 70%
    }

    /// <summary>Hasil hitung achievement satu section (atau baris total).</summary>
    public sealed record SectionAchievement(int Target, int SudahIsi, int BelumIsi, decimal Persen, AchievementLevel Level);

    /// <summary>
    /// Aturan hitung "Pengisian FTW per Section" di Dashboard (logika murni, mudah di-test).
    /// - Hari     : target = jumlah karyawan aktif di section.
    /// - Minggu/Bulan : target = jumlah karyawan x hari operasional (hari yang ada minimal 1 pengisian),
    ///   sama dengan doughnut "Karyawan vs Sudah Register".
    /// - Sudah isi = jumlah pasangan (karyawan, tanggal) yang mengisi; tidak pernah melebihi target.
    /// </summary>
    public static class FtwSectionRules
    {
        public const decimal BatasHijau = 90m;
        public const decimal BatasKuning = 70m;

        public static SectionAchievement Compute(int jumlahKaryawan, int sudahIsi, int hariOperasional)
        {
            var hari = Math.Max(hariOperasional, 1);
            var target = Math.Max(jumlahKaryawan, 0) * hari;
            var sudah = Math.Clamp(sudahIsi, 0, target);
            var persen = target <= 0 ? 0m : Math.Round(sudah * 100m / target, 1);
            return new SectionAchievement(target, sudah, target - sudah, persen, LevelOf(persen));
        }

        public static AchievementLevel LevelOf(decimal persen) =>
            persen >= BatasHijau ? AchievementLevel.Hijau
            : persen >= BatasKuning ? AchievementLevel.Kuning
            : AchievementLevel.Merah;

        /// <summary>Nama level untuk JSON / CSS: "hijau" | "kuning" | "merah".</summary>
        public static string LevelKey(AchievementLevel level) => level.ToString().ToLowerInvariant();
    }
}
