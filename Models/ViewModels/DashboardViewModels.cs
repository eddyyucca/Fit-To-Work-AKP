namespace FitToWorkApp.Models.ViewModels
{
    // Ringkasan hari ini (dihitung per karyawan: kalau isi ulang, yang dipakai status terakhir)
    public class DashboardTodaySummary
    {
        public int TotalKaryawan { get; set; }
        public int SudahIsi { get; set; }
        public int Fit { get; set; }
        public int TidakFit { get; set; }
        public int BelumIsi => Math.Max(TotalKaryawan - SudahIsi, 0);
    }

    // Satu baris per tanggal untuk line chart & bar chart 20 hari
    public class DashboardDailyRow
    {
        public DateTime Tanggal { get; set; }
        public int Total { get; set; }
        public int NonShift { get; set; }
        public int ShiftPagi { get; set; }
        public int ShiftMalam { get; set; }
        public int Fit { get; set; }
        public int TidakFit { get; set; }
    }

    // Bahan doughnut chart karyawan vs yang sudah isi
    public class DashboardCoverageRow
    {
        public int TotalKaryawan { get; set; }
        public int HariOperasional { get; set; }
        public int SudahIsi { get; set; }   // jumlah pasangan (karyawan, tanggal) yang sudah isi
    }

    public class DashboardTopRow
    {
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? Jabatan { get; set; }
        public string? Section { get; set; }
        public int JumlahHari { get; set; }
        public int TidakFit { get; set; }
        public DateTime TerakhirUtc { get; set; }
    }

    // Dipakai untuk tabel "Pengisian Terbaru" dan payload toastr realtime
    public class DashboardSubmissionRow
    {
        public int Id { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? Jabatan { get; set; }
        public string? Section { get; set; }
        public string JadwalKerja { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;   // FIT / TIDAK FIT
        public DateTime CreatedAtUtc { get; set; }
    }

    // Bentuk JSON yang dikirim ke browser (lewat SignalR & endpoint DashboardData)
    public class DashboardSubmissionEvent
    {
        public int Id { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Jabatan { get; set; } = "-";
        public string Section { get; set; } = "-";
        public string JadwalKerja { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsFit { get; set; }
        public string Jam { get; set; } = string.Empty;

        public static DashboardSubmissionEvent From(DashboardSubmissionRow r, DateTime localTime) => new()
        {
            Id = r.Id,
            NIP = r.NIP,
            Nama = r.Nama,
            Jabatan = string.IsNullOrWhiteSpace(r.Jabatan) ? "-" : r.Jabatan,
            Section = string.IsNullOrWhiteSpace(r.Section) ? "-" : r.Section,
            JadwalKerja = r.JadwalKerja,
            Status = r.Status,
            IsFit = r.Status == "FIT",
            Jam = localTime.ToString("HH:mm")
        };
    }
}
