using System.Globalization;

namespace FitToWorkApp.Services
{
    /// <summary>Cara pengisian Fit To Work "dibuka lagi" setelah karyawan mengisi.</summary>
    public enum FtwResetMode
    {
        /// <summary>Boleh isi lagi setelah jeda N menit sejak pengisian terakhir (default 12 jam).</summary>
        Jeda,

        /// <summary>Boleh isi lagi setelah melewati salah satu jam reset (mis. 06:00 &amp; 18:00, pola shift).</summary>
        JamTetap
    }

    /// <summary>
    /// Pengaturan pengisian FTW (menu Fit To Work › Pengaturan Pengisian), disimpan di dbo.AppSettings.
    /// Semua jam memakai zona waktu site (IAppClock), bukan UTC.
    /// </summary>
    public sealed record FtwSettings(
        FtwResetMode Mode,
        int JedaMenit,
        IReadOnlyList<TimeSpan> JamReset,
        bool IzinIsiUlang,
        int DetikKembali)
    {
        /// <summary>Dipakai kalau admin belum pernah menyimpan pengaturan (atau tabel belum ada).</summary>
        public static FtwSettings Default { get; } = new(
            FtwResetMode.Jeda,
            12 * 60,
            new[] { new TimeSpan(6, 0, 0), new TimeSpan(18, 0, 0) },
            true,
            8);
    }

    /// <summary>Key di dbo.AppSettings. JANGAN ganti string yang sudah dipakai.</summary>
    public static class FtwSettingKeys
    {
        public const string ResetMode = "Ftw.ResetMode";
        public const string JedaMenit = "Ftw.JedaMenit";
        public const string JamReset = "Ftw.JamReset";
        public const string IzinIsiUlang = "Ftw.IzinIsiUlang";
        public const string DetikKembali = "Ftw.DetikKembali";

        public static readonly IReadOnlyList<string> All = new[] { ResetMode, JedaMenit, JamReset, IzinIsiUlang, DetikKembali };
    }

    /// <summary>
    /// Logika murni periode pengisian FTW (tanpa database / HttpContext) supaya mudah di-unit-test.
    /// "Terkunci" = karyawan sudah mengisi di periode ini → yang tampil halaman "Sudah mengisi", bukan form.
    /// </summary>
    public static class FtwRules
    {
        public const int MinJedaMenit = 30;
        public const int MaxJedaMenit = 72 * 60;
        public const int MaxJamReset = 6;
        public const int MinDetikKembali = 3;
        public const int MaxDetikKembali = 60;

        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        /// <summary>Jam (lokal) paling awal karyawan boleh mengisi lagi setelah mengisi pada <paramref name="lastLocal"/>.</summary>
        public static DateTime NextOpenLocal(DateTime lastLocal, FtwSettings s)
        {
            if (s.Mode == FtwResetMode.JamTetap && s.JamReset.Count > 0)
            {
                var times = s.JamReset.OrderBy(t => t).ToList();
                // Jam reset selalu < 24 jam, jadi paling lambat ketemu di hari berikutnya
                for (var i = 0; i < 2; i++)
                {
                    var day = lastLocal.Date.AddDays(i);
                    foreach (var t in times)
                    {
                        var candidate = day + t;
                        if (candidate > lastLocal)
                            return candidate;
                    }
                }
            }

            return lastLocal.AddMinutes(Math.Clamp(s.JedaMenit, MinJedaMenit, MaxJedaMenit));
        }

        /// <summary>True = masih di periode yang sama dengan pengisian terakhir (belum boleh isi lagi tanpa "Isi Ulang").</summary>
        public static bool IsLocked(DateTime lastLocal, DateTime nowLocal, FtwSettings s) =>
            nowLocal < NextOpenLocal(lastLocal, s);

        // ---------- Simpan / baca dari dbo.AppSettings ----------

        /// <summary>Bangun pengaturan dari isi tabel. Nilai kosong / rusak = pakai default untuk bagian itu saja.</summary>
        public static FtwSettings FromValues(IReadOnlyDictionary<string, string> values)
        {
            var d = FtwSettings.Default;

            var mode = values.TryGetValue(FtwSettingKeys.ResetMode, out var m) && Enum.TryParse<FtwResetMode>(m, ignoreCase: true, out var pm)
                       && Enum.IsDefined(pm)
                ? pm : d.Mode;

            var jeda = values.TryGetValue(FtwSettingKeys.JedaMenit, out var j)
                       && int.TryParse(j, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pj)
                       && pj is >= MinJedaMenit and <= MaxJedaMenit
                ? pj : d.JedaMenit;

            var jam = values.TryGetValue(FtwSettingKeys.JamReset, out var r) && TryParseTimes(r, out var pr) && pr.Count is > 0 and <= MaxJamReset
                ? pr : d.JamReset;

            var izin = values.TryGetValue(FtwSettingKeys.IzinIsiUlang, out var iz) ? iz != "0" : d.IzinIsiUlang;

            var detik = values.TryGetValue(FtwSettingKeys.DetikKembali, out var dk)
                        && int.TryParse(dk, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pd)
                        && pd is >= MinDetikKembali and <= MaxDetikKembali
                ? pd : d.DetikKembali;

            return new FtwSettings(mode, jeda, jam, izin, detik);
        }

        public static Dictionary<string, string> ToValues(FtwSettings s) => new()
        {
            [FtwSettingKeys.ResetMode] = s.Mode.ToString(),
            [FtwSettingKeys.JedaMenit] = s.JedaMenit.ToString(CultureInfo.InvariantCulture),
            [FtwSettingKeys.JamReset] = string.Join(",", s.JamReset.OrderBy(t => t).Select(FormatTime)),
            [FtwSettingKeys.IzinIsiUlang] = s.IzinIsiUlang ? "1" : "0",
            [FtwSettingKeys.DetikKembali] = s.DetikKembali.ToString(CultureInfo.InvariantCulture)
        };

        /// <summary>Pesan kesalahan untuk form admin (kosong = valid).</summary>
        public static List<string> Validate(FtwSettings s)
        {
            var errors = new List<string>();

            if (s.JedaMenit < MinJedaMenit || s.JedaMenit > MaxJedaMenit)
                errors.Add($"Jeda harus antara {FormatJeda(MinJedaMenit)} dan {FormatJeda(MaxJedaMenit)}.");

            if (s.Mode == FtwResetMode.JamTetap)
            {
                if (s.JamReset.Count == 0)
                    errors.Add("Isi minimal 1 jam reset.");
                if (s.JamReset.Count > MaxJamReset)
                    errors.Add($"Jam reset maksimal {MaxJamReset}.");
                if (s.JamReset.Any(t => t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)))
                    errors.Add("Jam reset harus antara 00:00 dan 23:59.");
                if (s.JamReset.Distinct().Count() != s.JamReset.Count)
                    errors.Add("Jam reset tidak boleh ada yang sama.");
            }

            if (s.DetikKembali < MinDetikKembali || s.DetikKembali > MaxDetikKembali)
                errors.Add($"Lama tampil halaman \"sudah mengisi\" harus antara {MinDetikKembali} dan {MaxDetikKembali} detik.");

            return errors;
        }

        // ---------- Parse & format ----------

        /// <summary>"6:00", "06:00", "18:30" → TimeSpan (detik dibuang). Selain itu false.</summary>
        public static bool TryParseTime(string? text, out TimeSpan time)
        {
            time = default;
            var parts = (text ?? string.Empty).Trim().Split(':');
            if (parts.Length < 2 || parts.Length > 3)
                return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h is < 0 or > 23)
                return false;
            if (parts[1].Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 0 or > 59)
                return false;
            time = new TimeSpan(h, m, 0);
            return true;
        }

        /// <summary>"06:00,18:00" → daftar jam terurut. Satu saja yang rusak = false.</summary>
        public static bool TryParseTimes(string? text, out List<TimeSpan> times)
        {
            times = new List<TimeSpan>();
            foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseTime(part, out var t))
                    return false;
                times.Add(t);
            }
            times.Sort();
            return true;
        }

        public static string FormatTime(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

        /// <summary>720 → "12 jam", 90 → "1 jam 30 mnt", 30 → "30 mnt".</summary>
        public static string FormatJeda(int menit)
        {
            var jam = menit / 60;
            var sisa = menit % 60;
            if (jam == 0) return $"{sisa} mnt";
            return sisa == 0 ? $"{jam} jam" : $"{jam} jam {sisa} mnt";
        }

        /// <summary>Kalimat ringkas aturan aktif, mis. untuk halaman admin.</summary>
        public static string Describe(FtwSettings s) => s.Mode == FtwResetMode.JamTetap
            ? "Pengisian dibuka lagi setiap pukul " + JoinId(s.JamReset.OrderBy(t => t).Select(FormatTime).ToList())
            : $"Pengisian dibuka lagi {FormatJeda(s.JedaMenit)} setelah pengisian terakhir";

        /// <summary>Jam buka berikutnya relatif terhadap sekarang: "pukul 18:10", "besok pukul 06:00", atau tanggal lengkap.</summary>
        public static string FormatNextOpen(DateTime nextLocal, DateTime nowLocal)
        {
            var jam = nextLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (nextLocal.Date == nowLocal.Date) return $"pukul {jam}";
            if (nextLocal.Date == nowLocal.Date.AddDays(1)) return $"besok pukul {jam}";
            return nextLocal.ToString("dddd, dd/MM/yyyy", Id) + $" pukul {jam}";
        }

        private static string JoinId(IReadOnlyList<string> items) => items.Count switch
        {
            0 => "-",
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " dan " + items[^1]
        };
    }
}
