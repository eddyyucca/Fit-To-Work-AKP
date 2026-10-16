namespace FitToWorkApp.Models
{
    public class FitToWorkSubmission : ILocationStamped
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string? NomorHp { get; set; }
        public string JadwalKerja { get; set; } = string.Empty;
        public string AreaKerja { get; set; } = string.Empty;
        public string? AreaKerjaLainnya { get; set; }
        public string JamTidur { get; set; } = string.Empty;
        public string KualitasTidur { get; set; } = string.Empty;
        public string GejalaKesehatan { get; set; } = string.Empty;
        public string KondisiFokus { get; set; } = string.Empty;
        public string KonsumsiObat { get; set; } = string.Empty;
        public string KesiapanKerja { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        // Jejak lokasi saat mengisi (kunci lokasi, lihat migration_site_location.sql)
        public int? LokasiSiteId { get; set; }
        public string? LokasiNama { get; set; }
        public decimal? GpsLat { get; set; }
        public decimal? GpsLng { get; set; }
        public int? GpsAkurasi { get; set; }
        public int? JarakMeter { get; set; }
        public string? LokasiVia { get; set; }

        // Dipakai saat JOIN ke Employees untuk daftar riwayat (bukan kolom tabel)
        public string? NIP { get; set; }
    }
}
