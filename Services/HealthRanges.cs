using System.Globalization;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    public enum HealthLevel { None, Normal, Perhatian, Tinggi, Rendah }

    public record HealthFlag(HealthLevel Level, string Label)
    {
        public static readonly HealthFlag None = new(HealthLevel.None, "");

        public string BadgeClass => Level switch
        {
            HealthLevel.Normal => "bg-success",
            HealthLevel.Perhatian => "bg-warning text-dark",
            HealthLevel.Tinggi => "bg-danger",
            HealthLevel.Rendah => "bg-info text-dark",
            _ => "bg-secondary"
        };

        public bool IsAbnormal => Level is HealthLevel.Perhatian or HealthLevel.Tinggi or HealthLevel.Rendah;
    }

    /// <summary>Satu angka acuan yang bisa diubah di menu Acuan Nilai Pemkes.</summary>
    public record HealthReferenceDefinition(
        string Key, string Group, string Label, string Unit,
        decimal Default, decimal Min, decimal Max, int Decimals, string? Hint = null)
    {
        /// <summary>
        /// Batas pemkes format lama (tensi, kolesterol, gula, dll.). Sejak ada menu Parameter &amp; Skor Pemkes
        /// hanya dipakai untuk menampilkan penanda pada pemeriksaan LAMA, jadi tidak ditampilkan lagi di menu.
        /// </summary>
        public bool IsLegacy => Key is not (HealthReferenceKeys.AlkoholBatas or HealthReferenceKeys.FollowUpDefaultHari);
    }

    /// <summary>Key acuan nilai (disimpan di dbo.HealthReferenceValues). JANGAN ganti string yang sudah dipakai.</summary>
    public static class HealthReferenceKeys
    {
        public const string TdTinggiSistolik = "TdTinggiSistolik";
        public const string TdTinggiDiastolik = "TdTinggiDiastolik";
        public const string TdPerhatianSistolik = "TdPerhatianSistolik";
        public const string TdPerhatianDiastolik = "TdPerhatianDiastolik";
        public const string TdRendahSistolik = "TdRendahSistolik";
        public const string TdRendahDiastolik = "TdRendahDiastolik";
        public const string KolesterolPerhatian = "KolesterolPerhatian";
        public const string KolesterolTinggi = "KolesterolTinggi";
        public const string AsamUratMaxPria = "AsamUratMaxPria";
        public const string AsamUratMaxWanita = "AsamUratMaxWanita";
        public const string LingkarPerutMaxPria = "LingkarPerutMaxPria";
        public const string LingkarPerutMaxWanita = "LingkarPerutMaxWanita";
        public const string GulaRendah = "GulaRendah";
        public const string GulaSewaktuPerhatian = "GulaSewaktuPerhatian";
        public const string GulaSewaktuTinggi = "GulaSewaktuTinggi";
        public const string GulaPuasaPerhatian = "GulaPuasaPerhatian";
        public const string GulaPuasaTinggi = "GulaPuasaTinggi";
        public const string ImtKurang = "ImtKurang";
        public const string ImtLebih = "ImtLebih";
        public const string ImtObesitas = "ImtObesitas";
        public const string AlkoholBatas = "AlkoholBatas";
        public const string FollowUpDefaultHari = "FollowUpDefaultHari";
    }

    /// <summary>
    /// Acuan nilai normal pemeriksaan kesehatan untuk penanda warna (bukan diagnosis).
    /// Nilai default ada di <see cref="Definitions"/>; nilai yang diubah admin (menu Acuan Nilai Pemkes)
    /// disimpan di dbo.HealthReferenceValues dan dimuat lewat <see cref="Apply"/> saat aplikasi start & setelah disimpan.
    /// Nilai yang sama dikirim ke JavaScript (form input) lewat <see cref="ToClientConfig"/>.
    /// </summary>
    public static class HealthRanges
    {
        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        public static readonly IReadOnlyList<HealthReferenceDefinition> Definitions = new[]
        {
            new HealthReferenceDefinition(HealthReferenceKeys.TdPerhatianSistolik, "Tekanan darah", "Sistolik mulai 'normal tinggi' (≥)", "mmHg", 120, 80, 250, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.TdPerhatianDiastolik, "Tekanan darah", "Diastolik mulai 'normal tinggi' (≥)", "mmHg", 80, 40, 150, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.TdTinggiSistolik, "Tekanan darah", "Sistolik tinggi (≥)", "mmHg", 140, 80, 250, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.TdTinggiDiastolik, "Tekanan darah", "Diastolik tinggi (≥)", "mmHg", 90, 40, 150, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.TdRendahSistolik, "Tekanan darah", "Sistolik rendah (<)", "mmHg", 90, 50, 200, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.TdRendahDiastolik, "Tekanan darah", "Diastolik rendah (<)", "mmHg", 60, 30, 150, 0),

            new HealthReferenceDefinition(HealthReferenceKeys.KolesterolPerhatian, "Kolesterol total", "Batas tinggi (≥)", "mg/dL", 200, 100, 500, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.KolesterolTinggi, "Kolesterol total", "Tinggi (≥)", "mg/dL", 240, 100, 600, 0),

            new HealthReferenceDefinition(HealthReferenceKeys.AsamUratMaxPria, "Asam urat", "Tinggi pria (>)", "mg/dL", 7.0m, 2, 15, 1),
            new HealthReferenceDefinition(HealthReferenceKeys.AsamUratMaxWanita, "Asam urat", "Tinggi wanita (>)", "mg/dL", 6.0m, 2, 15, 1,
                "Kalau jenis kelamin karyawan belum diisi, acuan pria dipakai dan nilai di antara kedua batas diberi tanda kuning."),

            new HealthReferenceDefinition(HealthReferenceKeys.LingkarPerutMaxPria, "Lingkar perut", "Obesitas sentral pria (≥)", "cm", 90, 50, 200, 1),
            new HealthReferenceDefinition(HealthReferenceKeys.LingkarPerutMaxWanita, "Lingkar perut", "Obesitas sentral wanita (≥)", "cm", 80, 50, 200, 1),

            new HealthReferenceDefinition(HealthReferenceKeys.GulaRendah, "Gula darah", "Rendah (<)", "mg/dL", 70, 20, 200, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.GulaSewaktuPerhatian, "Gula darah", "Sewaktu / 2 jam PP: batas tinggi (≥)", "mg/dL", 140, 50, 600, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.GulaSewaktuTinggi, "Gula darah", "Sewaktu / 2 jam PP: tinggi (≥)", "mg/dL", 200, 50, 800, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.GulaPuasaPerhatian, "Gula darah", "Puasa: batas tinggi (≥)", "mg/dL", 100, 50, 600, 0),
            new HealthReferenceDefinition(HealthReferenceKeys.GulaPuasaTinggi, "Gula darah", "Puasa: tinggi (≥)", "mg/dL", 126, 50, 800, 0),

            new HealthReferenceDefinition(HealthReferenceKeys.ImtKurang, "IMT (Indeks Massa Tubuh)", "Berat kurang (<)", "kg/m²", 18.5m, 10, 40, 1),
            new HealthReferenceDefinition(HealthReferenceKeys.ImtLebih, "IMT (Indeks Massa Tubuh)", "Berat lebih (≥)", "kg/m²", 23m, 10, 50, 1),
            new HealthReferenceDefinition(HealthReferenceKeys.ImtObesitas, "IMT (Indeks Massa Tubuh)", "Obesitas (≥)", "kg/m²", 25m, 10, 60, 1,
                "Default mengikuti acuan Asia-Pasifik."),

            new HealthReferenceDefinition(HealthReferenceKeys.AlkoholBatas, "Tes alkohol", "Positif jika kadar lebih dari (>)", "satuan alat", 0m, 0, 10, 3,
                "Isi dengan satuan yang sama dengan alat tes (mis. %BAC atau mg/L). 0 = zero tolerance: kadar berapa pun di atas 0 dianggap positif."),

            new HealthReferenceDefinition(HealthReferenceKeys.FollowUpDefaultHari, "Follow up pemeriksaan", "Usulan tanggal follow up (hari setelah pemeriksaan)", "hari", 30, 1, 365, 0,
                "Dipakai untuk mengisi otomatis tanggal follow up saat ada temuan. Tetap bisa diubah di form.")
        };

        private static readonly IReadOnlyDictionary<string, HealthReferenceDefinition> DefinitionByKey =
            Definitions.ToDictionary(d => d.Key);

        private static IReadOnlyDictionary<string, decimal> _values = Definitions.ToDictionary(d => d.Key, d => d.Default);

        /// <summary>Nilai yang sedang berlaku (default + yang diubah admin).</summary>
        public static IReadOnlyDictionary<string, decimal> Current => _values;

        public static HealthReferenceDefinition? Definition(string key) => DefinitionByKey.TryGetValue(key, out var d) ? d : null;

        /// <summary>Terapkan nilai dari database. Key yang tidak ada = pakai default; key tak dikenal diabaikan.</summary>
        public static void Apply(IReadOnlyDictionary<string, decimal> overrides)
        {
            var merged = Definitions.ToDictionary(d => d.Key,
                d => overrides.TryGetValue(d.Key, out var v) ? Math.Round(v, d.Decimals) : d.Default);
            _values = merged;   // tukar referensi sekaligus (aman dibaca request lain yang sedang berjalan)
        }

        public static decimal Get(string key) => _values.TryGetValue(key, out var v) ? v : DefinitionByKey[key].Default;
        private static int GetInt(string key) => (int)Math.Round(Get(key));

        /// <summary>Cek urutan batas masuk akal (mis. batas "tinggi" harus di atas "perhatian").</summary>
        public static List<string> Validate(IReadOnlyDictionary<string, decimal> v)
        {
            var errors = new List<string>();
            decimal V(string k) => v.TryGetValue(k, out var x) ? x : DefinitionByKey[k].Default;
            void Less(string low, string high, bool orEqual = false)
            {
                var ok = orEqual ? V(low) <= V(high) : V(low) < V(high);
                if (!ok)
                    errors.Add($"{DefinitionByKey[low].Group}: \"{DefinitionByKey[low].Label}\" harus lebih kecil dari \"{DefinitionByKey[high].Label}\".");
            }

            foreach (var d in Definitions)
            {
                var x = V(d.Key);
                if (x < d.Min || x > d.Max)
                    errors.Add($"{d.Group}: \"{d.Label}\" harus antara {Fmt(d.Min)} dan {Fmt(d.Max)} {d.Unit}.");
            }

            Less(HealthReferenceKeys.TdRendahSistolik, HealthReferenceKeys.TdPerhatianSistolik);
            Less(HealthReferenceKeys.TdPerhatianSistolik, HealthReferenceKeys.TdTinggiSistolik);
            Less(HealthReferenceKeys.TdRendahDiastolik, HealthReferenceKeys.TdPerhatianDiastolik);
            Less(HealthReferenceKeys.TdPerhatianDiastolik, HealthReferenceKeys.TdTinggiDiastolik);
            Less(HealthReferenceKeys.KolesterolPerhatian, HealthReferenceKeys.KolesterolTinggi);
            Less(HealthReferenceKeys.GulaRendah, HealthReferenceKeys.GulaSewaktuPerhatian);
            Less(HealthReferenceKeys.GulaSewaktuPerhatian, HealthReferenceKeys.GulaSewaktuTinggi);
            Less(HealthReferenceKeys.GulaRendah, HealthReferenceKeys.GulaPuasaPerhatian);
            Less(HealthReferenceKeys.GulaPuasaPerhatian, HealthReferenceKeys.GulaPuasaTinggi);
            Less(HealthReferenceKeys.ImtKurang, HealthReferenceKeys.ImtLebih);
            Less(HealthReferenceKeys.ImtLebih, HealthReferenceKeys.ImtObesitas);
            return errors;
        }

        /// <summary>Format angka gaya Indonesia (koma desimal), tanpa nol berlebih.</summary>
        public static string Fmt(decimal v) => v.ToString("0.###", Id);

        // ---------- Nilai yang berlaku (dipakai logika penanda) ----------
        public static int TdTinggiSistolik => GetInt(HealthReferenceKeys.TdTinggiSistolik);
        public static int TdTinggiDiastolik => GetInt(HealthReferenceKeys.TdTinggiDiastolik);
        public static int TdPerhatianSistolik => GetInt(HealthReferenceKeys.TdPerhatianSistolik);
        public static int TdPerhatianDiastolik => GetInt(HealthReferenceKeys.TdPerhatianDiastolik);
        public static int TdRendahSistolik => GetInt(HealthReferenceKeys.TdRendahSistolik);
        public static int TdRendahDiastolik => GetInt(HealthReferenceKeys.TdRendahDiastolik);
        public static int KolesterolPerhatian => GetInt(HealthReferenceKeys.KolesterolPerhatian);
        public static int KolesterolTinggi => GetInt(HealthReferenceKeys.KolesterolTinggi);
        public static decimal AsamUratMaxPria => Get(HealthReferenceKeys.AsamUratMaxPria);
        public static decimal AsamUratMaxWanita => Get(HealthReferenceKeys.AsamUratMaxWanita);
        public static decimal LingkarPerutMaxPria => Get(HealthReferenceKeys.LingkarPerutMaxPria);
        public static decimal LingkarPerutMaxWanita => Get(HealthReferenceKeys.LingkarPerutMaxWanita);
        public static int GulaRendah => GetInt(HealthReferenceKeys.GulaRendah);
        public static int GulaSewaktuPerhatian => GetInt(HealthReferenceKeys.GulaSewaktuPerhatian);
        public static int GulaSewaktuTinggi => GetInt(HealthReferenceKeys.GulaSewaktuTinggi);
        public static int GulaPuasaPerhatian => GetInt(HealthReferenceKeys.GulaPuasaPerhatian);
        public static int GulaPuasaTinggi => GetInt(HealthReferenceKeys.GulaPuasaTinggi);
        public static decimal ImtKurang => Get(HealthReferenceKeys.ImtKurang);
        public static decimal ImtLebih => Get(HealthReferenceKeys.ImtLebih);
        public static decimal ImtObesitas => Get(HealthReferenceKeys.ImtObesitas);
        public static decimal AlkoholBatas => Get(HealthReferenceKeys.AlkoholBatas);
        public static int FollowUpDefaultHari => GetInt(HealthReferenceKeys.FollowUpDefaultHari);

        public static HealthFlag TekananDarah(int? sis, int? dia)
        {
            if (sis is null || dia is null) return HealthFlag.None;
            if (sis >= TdTinggiSistolik || dia >= TdTinggiDiastolik) return new(HealthLevel.Tinggi, "Tinggi");
            if (sis < TdRendahSistolik || dia < TdRendahDiastolik) return new(HealthLevel.Rendah, "Rendah");
            if (sis >= TdPerhatianSistolik || dia >= TdPerhatianDiastolik) return new(HealthLevel.Perhatian, "Normal tinggi");
            return new(HealthLevel.Normal, "Normal");
        }

        public static HealthFlag Kolesterol(int? v)
        {
            if (v is null) return HealthFlag.None;
            if (v >= KolesterolTinggi) return new(HealthLevel.Tinggi, "Tinggi");
            if (v >= KolesterolPerhatian) return new(HealthLevel.Perhatian, "Batas tinggi");
            return new(HealthLevel.Normal, "Normal");
        }

        public static HealthFlag AsamUrat(decimal? v, string? jk)
        {
            if (v is null) return HealthFlag.None;
            var max = jk == "P" ? AsamUratMaxWanita : AsamUratMaxPria;
            if (v > max) return new(HealthLevel.Tinggi, "Tinggi");
            // Jenis kelamin belum diisi: nilai di antara batas wanita & pria diberi tanda perhatian
            if (jk is null && v > AsamUratMaxWanita) return new(HealthLevel.Perhatian, "Perhatian");
            return new(HealthLevel.Normal, "Normal");
        }

        public static HealthFlag LingkarPerut(decimal? v, string? jk)
        {
            if (v is null) return HealthFlag.None;
            var max = jk == "P" ? LingkarPerutMaxWanita : LingkarPerutMaxPria;
            if (v >= max) return new(HealthLevel.Tinggi, "Obesitas sentral");
            if (jk is null && v >= LingkarPerutMaxWanita) return new(HealthLevel.Perhatian, "Perhatian");
            return new(HealthLevel.Normal, "Normal");
        }

        public static HealthFlag GulaDarah(int? v, string jenis)
        {
            if (v is null) return HealthFlag.None;
            if (v < GulaRendah) return new(HealthLevel.Rendah, "Rendah");
            var (perhatian, tinggi) = jenis == GulaDarahTypes.Puasa
                ? (GulaPuasaPerhatian, GulaPuasaTinggi)
                : (GulaSewaktuPerhatian, GulaSewaktuTinggi);   // sewaktu & 2 jam PP pakai batas yang sama
            if (v >= tinggi) return new(HealthLevel.Tinggi, "Tinggi");
            if (v >= perhatian) return new(HealthLevel.Perhatian, "Batas tinggi");
            return new(HealthLevel.Normal, "Normal");
        }

        public static HealthFlag Imt(decimal? v)
        {
            if (v is null) return HealthFlag.None;
            if (v < ImtKurang) return new(HealthLevel.Rendah, "Berat kurang");
            if (v >= ImtObesitas) return new(HealthLevel.Tinggi, "Obesitas");
            if (v >= ImtLebih) return new(HealthLevel.Perhatian, "Berat lebih");
            return new(HealthLevel.Normal, "Normal");
        }

        /// <summary>Hasil tes alkohol dari kadar alat: lebih dari batas = Positif.</summary>
        public static string AlkoholHasil(decimal kadar) => kadar > AlkoholBatas ? TestResults.Positif : TestResults.Negatif;

        /// <summary>Semua penanda satu pemeriksaan (kunci = nama kolom).</summary>
        public static Dictionary<string, HealthFlag> Evaluate(HealthCheckup c) => new()
        {
            ["TekananDarah"] = TekananDarah(c.Sistolik, c.Diastolik),
            ["Kolesterol"] = Kolesterol(c.Kolesterol),
            ["AsamUrat"] = AsamUrat(c.AsamUrat, c.JenisKelamin),
            ["LingkarPerut"] = LingkarPerut(c.LingkarPerut, c.JenisKelamin),
            ["GulaDarah"] = GulaDarah(c.GulaDarah, c.GulaDarahJenis),
            ["Imt"] = Imt(c.Imt)
        };

        public static int AbnormalCount(HealthCheckup c) => Evaluate(c).Values.Count(f => f.IsAbnormal);

        /// <summary>Ringkasan acuan yang berlaku, untuk ditampilkan di samping form input.</summary>
        public static IReadOnlyList<string> Summary() => new[]
        {
            $"Tekanan darah: normal < {TdPerhatianSistolik}/{TdPerhatianDiastolik}, tinggi ≥ {TdTinggiSistolik}/{TdTinggiDiastolik}, rendah < {TdRendahSistolik}/{TdRendahDiastolik}",
            $"Kolesterol total: normal < {KolesterolPerhatian}, tinggi ≥ {KolesterolTinggi}",
            $"Asam urat: tinggi > {Fmt(AsamUratMaxPria)} (pria) / > {Fmt(AsamUratMaxWanita)} (wanita)",
            $"Lingkar perut: ≥ {Fmt(LingkarPerutMaxPria)} cm (pria) / ≥ {Fmt(LingkarPerutMaxWanita)} cm (wanita)",
            $"Gula sewaktu / 2 jam PP: tinggi ≥ {GulaSewaktuTinggi}; puasa: tinggi ≥ {GulaPuasaTinggi}; rendah < {GulaRendah}",
            $"IMT: normal {Fmt(ImtKurang)} – < {Fmt(ImtLebih)}; lebih ≥ {Fmt(ImtLebih)}; obesitas ≥ {Fmt(ImtObesitas)}"
        };

        /// <summary>Batas yang sama untuk dipakai JavaScript (penanda warna saat medic mengetik).</summary>
        public static object ToClientConfig() => new
        {
            td = new { tinggiSis = TdTinggiSistolik, tinggiDia = TdTinggiDiastolik, perhatianSis = TdPerhatianSistolik, perhatianDia = TdPerhatianDiastolik, rendahSis = TdRendahSistolik, rendahDia = TdRendahDiastolik },
            kolesterol = new { perhatian = KolesterolPerhatian, tinggi = KolesterolTinggi },
            asamUrat = new { pria = AsamUratMaxPria, wanita = AsamUratMaxWanita },
            lingkarPerut = new { pria = LingkarPerutMaxPria, wanita = LingkarPerutMaxWanita },
            gula = new { rendah = GulaRendah, sewaktuPerhatian = GulaSewaktuPerhatian, sewaktuTinggi = GulaSewaktuTinggi, puasaPerhatian = GulaPuasaPerhatian, puasaTinggi = GulaPuasaTinggi },
            imt = new { kurang = ImtKurang, lebih = ImtLebih, obesitas = ImtObesitas },
            alkohol = new { batas = AlkoholBatas },
            followUpHari = FollowUpDefaultHari
        };
    }
}
