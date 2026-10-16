namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Satu baris tabel "Pengisian FTW per Section" (hasil query, sebelum dihitung achievement-nya).</summary>
    public class DashboardSectionRow
    {
        public int? SectionId { get; set; }          // NULL = karyawan belum diisi section-nya
        public string? Section { get; set; }
        public string? Department { get; set; }
        public int JumlahKaryawan { get; set; }      // karyawan AKP aktif di section ini (data karyawan saat ini)
        public int SudahIsi { get; set; }            // jumlah pasangan (karyawan, tanggal) yang mengisi
        public int Fit { get; set; }                 // status terakhir per karyawan per hari
        public int TidakFit { get; set; }
        public int KaryawanPernahIsi { get; set; }   // karyawan yang minimal 1x mengisi dalam periode
    }

    /// <summary>Satu karyawan di daftar "belum isi / belum lengkap" saat section di-klik.</summary>
    public class DashboardSectionEmployeeRow
    {
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? Jabatan { get; set; }
        public int HariIsi { get; set; }
        public DateTime? TerakhirUtc { get; set; }   // pengisian terakhir (kapan pun, bukan hanya di periode)
    }
}
