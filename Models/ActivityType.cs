namespace FitToWorkApp.Models
{
    /// <summary>
    /// Master jenis aktivitas kebugaran (tabel dbo.ActivityTypes): Gym, Lari, Jalan Cepat, Hiking, dll.
    /// Setiap jenis punya aturan sendiri: pakai GPS, wajib berat badan, batas jam sesi, kunci lokasi.
    /// </summary>
    public class ActivityType
    {
        public int Id { get; set; }
        /// <summary>Kode unik huruf besar (mis. GYM, LARI). Tidak bisa diubah setelah dibuat.</summary>
        public string Kode { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        /// <summary>Emoji yang tampil di tombol pilihan (mis. 🏃).</summary>
        public string Ikon { get; set; } = "🏅";
        public bool PakaiGps { get; set; }
        public bool WajibBerat { get; set; }
        public int MaksJam { get; set; } = 3;
        /// <summary>Lihat <see cref="ActivityLocationLock"/>.</summary>
        public string KunciLokasi { get; set; } = ActivityLocationLock.Mulai;
        public decimal? MaksKecepatanKmh { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Jumlah sesi yang memakai jenis ini (hanya diisi di daftar admin).</summary>
        public int JumlahSesi { get; set; }

        public const string KodeGym = "GYM";
        public const int MinJam = 1;
        public const int MaxJam = 12;
    }

    /// <summary>Kunci lokasi per jenis aktivitas.</summary>
    public static class ActivityLocationLock
    {
        /// <summary>Mulai & selesai wajib di area site (mis. gym di dalam site).</summary>
        public const string MulaiSelesai = "MulaiSelesai";
        /// <summary>Hanya mulai yang wajib di area site; selesai boleh di mana saja (lari, hiking).</summary>
        public const string Mulai = "Mulai";
        /// <summary>Tanpa kunci lokasi.</summary>
        public const string Tidak = "Tidak";

        public static readonly IReadOnlyList<string> All = new[] { MulaiSelesai, Mulai, Tidak };

        public static bool IsValid(string? value) => value is not null && All.Contains(value);

        public static bool CheckAtStart(string? value) => value != Tidak;

        /// <summary>null (data lama gym) = dianggap MulaiSelesai.</summary>
        public static bool CheckAtFinish(string? value) => value is null || value == MulaiSelesai;

        public static string Label(string? value) => value switch
        {
            MulaiSelesai => "Mulai & selesai di area site",
            Mulai => "Mulai di area site",
            Tidak => "Tanpa kunci lokasi",
            _ => "-"
        };
    }
}
