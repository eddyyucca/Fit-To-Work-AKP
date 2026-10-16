namespace FitToWorkApp.Models
{
    /// <summary>
    /// Satu sesi aktivitas kebugaran (gym, lari, hiking, dll): mulai (CreatedAt) sampai selesai (CheckOutAt).
    /// Nama tabel tetap dbo.GymAttendances karena fitur ini berawal dari daftar hadir gym.
    /// Data diri & jenis aktivitas disimpan sebagai snapshot supaya riwayat tetap benar walau master berubah.
    /// Status sesi ada di <see cref="CheckOutStatus"/> (lihat <see cref="GymCheckOutStatus"/>).
    /// </summary>
    public class GymAttendance : ILocationStamped
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string DepartmentSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string PositionSnapshot { get; set; } = string.Empty;

        /// <summary>Berat badan saat mulai. Hanya wajib untuk jenis aktivitas tertentu (gym).</summary>
        public decimal? BeratBadan { get; set; }

        // Snapshot jenis aktivitas saat mulai
        public int? ActivityTypeId { get; set; }
        public string? AktivitasNama { get; set; }
        public string? AktivitasIkon { get; set; }
        public bool? PakaiGps { get; set; }
        public string? KunciLokasi { get; set; }

        /// <summary>Jam mulai (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Batas sesi (UTC) = mulai + batas jam jenis aktivitas. null = data lama (3 jam).</summary>
        public DateTime? BatasAt { get; set; }

        /// <summary>Jam selesai (UTC). null = belum / tidak diselesaikan.</summary>
        public DateTime? CheckOutAt { get; set; }

        /// <summary>null = sedang berlangsung, 'Manual', 'Auto' (tidak diselesaikan), 'Legacy' (data lama).</summary>
        public string? CheckOutStatus { get; set; }

        public int? CheckOutJarakMeter { get; set; }

        // Hasil GPS (hanya aktivitas PakaiGps)
        public int? JarakTempuhMeter { get; set; }
        public string? RutePolyline { get; set; }
        public int? GpsTitik { get; set; }
        public int? GpsCelahDetik { get; set; }

        // Jejak lokasi saat mulai
        public int? LokasiSiteId { get; set; }
        public string? LokasiNama { get; set; }
        public decimal? GpsLat { get; set; }
        public decimal? GpsLng { get; set; }
        public int? GpsAkurasi { get; set; }
        public int? JarakMeter { get; set; }
        public string? LokasiVia { get; set; }

        // Dipakai saat JOIN ke Employees (bukan kolom tabel)
        public string? NIP { get; set; }

        /// <summary>Ada rute GPS (dipakai di daftar yang tidak mengambil <see cref="RutePolyline"/> supaya ringan).</summary>
        public bool PunyaRute { get; set; }

        /// <summary>Nama aktivitas untuk tampilan (data lama = Gym).</summary>
        public string Aktivitas => string.IsNullOrWhiteSpace(AktivitasNama) ? "Gym" : AktivitasNama!;

        /// <summary>Ikon aktivitas untuk tampilan (data lama = 🏋️).</summary>
        public string Ikon => string.IsNullOrWhiteSpace(AktivitasIkon) ? "🏋️" : AktivitasIkon!;

        public bool IsGps => PakaiGps == true;

        /// <summary>Durasi aktivitas (menit). Hanya ada kalau karyawan menyelesaikan sendiri.</summary>
        public int? DurasiMenit => CheckOutAt.HasValue && CheckOutStatus == GymCheckOutStatus.Manual
            ? (int)Math.Max(0, Math.Floor((CheckOutAt.Value - CreatedAt).TotalMinutes))
            : null;

        /// <summary>Durasi dalam detik (untuk pace).</summary>
        public int? DurasiDetik => CheckOutAt.HasValue && CheckOutStatus == GymCheckOutStatus.Manual
            ? (int)Math.Max(0, Math.Floor((CheckOutAt.Value - CreatedAt).TotalSeconds))
            : null;
    }

    public static class GymCheckOutStatus
    {
        public const string Manual = "Manual";   // diselesaikan sendiri, durasi dihitung
        public const string Auto = "Auto";       // tidak diselesaikan, ditutup sistem setelah batas jam, durasi tidak dihitung
        public const string Legacy = "Legacy";   // daftar hadir gym sebelum ada fitur check-out
    }
}
