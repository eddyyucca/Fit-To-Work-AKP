namespace FitToWorkApp.Models
{
    /// <summary>
    /// Titik lokasi site untuk kunci pengisian Fit To Work & Gym (tabel dbo.SiteLocations).
    /// Karyawan hanya boleh mengisi kalau posisi HP-nya di dalam radius salah satu lokasi aktif,
    /// atau HP-nya terhubung ke Wi-Fi site (IP publik cocok dengan <see cref="IpPublik"/>).
    /// </summary>
    public class SiteLocation
    {
        public int Id { get; set; }
        public string Nama { get; set; } = string.Empty;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public int RadiusMeter { get; set; }

        /// <summary>IP publik internet Wi-Fi site, boleh lebih dari satu (dipisah koma). Opsional.</summary>
        public string? IpPublik { get; set; }

        public string? Keterangan { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public const int MinRadius = 50;
        public const int MaxRadius = 20000;

        /// <summary>Daftar IP dari kolom <see cref="IpPublik"/> (sudah di-trim, tanpa entri kosong).</summary>
        public IReadOnlyList<string> IpList =>
            string.IsNullOrWhiteSpace(IpPublik)
                ? Array.Empty<string>()
                : IpPublik.Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Kolom jejak lokasi yang sama di FitToWorkSubmissions & GymAttendances (lihat migration_site_location.sql).
    /// </summary>
    public interface ILocationStamped
    {
        int? LokasiSiteId { get; set; }
        string? LokasiNama { get; set; }
        decimal? GpsLat { get; set; }
        decimal? GpsLng { get; set; }
        int? GpsAkurasi { get; set; }
        int? JarakMeter { get; set; }
        /// <summary>'GPS' | 'WIFI' | null (kunci lokasi belum aktif / data lama)</summary>
        string? LokasiVia { get; set; }
    }
}
