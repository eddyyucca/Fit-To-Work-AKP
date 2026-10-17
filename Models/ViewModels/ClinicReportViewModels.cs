namespace FitToWorkApp.Models.ViewModels
{
    // Semua model di sini hanya untuk menampilkan laporan (baca-saja), tidak ada yang disimpan ke database.

    public class ReportSummary
    {
        public int TotalKunjungan { get; set; }
        public int KaryawanUnik { get; set; }
        public int KembaliBekerja { get; set; }
        public int KerjaTerbatas { get; set; }
        public int Istirahat { get; set; }
        public int Rujuk { get; set; }
        public int TotalHariIstirahat { get; set; }
        public int ObatKeluar { get; set; }
    }

    public class ReportDailyRow
    {
        public int Bucket { get; set; }   // selisih hari dari tanggal awal (tanggal lokal site)
        public int Jumlah { get; set; }
    }

    public class ReportDeptRow
    {
        public string Department { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
        public int HariIstirahat { get; set; }
        public int Rujuk { get; set; }
        public int? KaryawanAktif { get; set; }   // null = department tidak dikenal di master (mis. karyawan mitra)

        /// <summary>Kunjungan per 100 karyawan aktif. Hanya bermakna kalau jumlah karyawan aktif diketahui.</summary>
        public decimal? PerSeratus => KaryawanAktif is > 0 ? Math.Round(Jumlah * 100m / KaryawanAktif.Value, 1) : null;
    }

    public class ReportNameCountRow
    {
        public string Nama { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
    }

    public class ReportDiseaseRow
    {
        public string? Kode { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string? Kategori { get; set; }
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
    }

    public class ReportDiseaseMonthRow
    {
        public DateTime Bulan { get; set; }
        public string Nama { get; set; } = string.Empty;
        public int Jumlah { get; set; }
    }

    public class ReportRepeatVisitorRow
    {
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int HariIstirahat { get; set; }
        public DateTime TerakhirUtc { get; set; }
    }

    public class ReportProfileRow
    {
        public string JenisKelamin { get; set; } = string.Empty;   // L / P / "" (tidak diketahui)
        public string KelompokUmur { get; set; } = string.Empty;
        public int Jumlah { get; set; }
    }

    public class ReportHeatRow
    {
        public int Hari { get; set; }   // 0 = Senin ... 6 = Minggu
        public int Jam { get; set; }    // 0-23 (waktu lokal site)
        public int Jumlah { get; set; }
    }

    public class ReportMedicineUseRow
    {
        public string Nama { get; set; } = string.Empty;
        public string Satuan { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int Kunjungan { get; set; }
    }

    public class ReportStockRow
    {
        public int Id { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string Satuan { get; set; } = string.Empty;
        public int Stok { get; set; }
        public int StokMinimum { get; set; }
        public int Qty30Hari { get; set; }   // total keluar 30 hari terakhir

        public decimal PemakaianPerHari => Math.Round(Qty30Hari / 30m, 2);

        /// <summary>Perkiraan stok habis (hari), dari laju pemakaian 30 hari terakhir. Null kalau tidak ada pemakaian.</summary>
        public int? PerkiraanHariHabis => Qty30Hari > 0 ? (int)Math.Floor(Stok / (Qty30Hari / 30m)) : null;

        public bool DiBawahMinimum => Stok <= StokMinimum;
    }

    // ------------------------- Pemeriksaan kesehatan -------------------------

    public class ReportCheckupSummary
    {
        public int Total { get; set; }
        public int Karyawan { get; set; }
        public int AdaTemuan { get; set; }
        public int KaryawanAktif { get; set; }       // karyawan AKP aktif (sesuai filter department)
        public int FollowUpBelumSelesai { get; set; }   // semua periode
        public int FollowUpTerlambat { get; set; }      // semua periode, lewat tanggal follow up

        public decimal? CakupanPersen => KaryawanAktif > 0 ? Math.Round(Karyawan * 100m / KaryawanAktif, 1) : null;
    }

    public class ReportCheckupDeptRow
    {
        public string Department { get; set; } = string.Empty;
        public int Total { get; set; }
        public decimal? RataSistolik { get; set; }
        public decimal? RataDiastolik { get; set; }
        public int TdTinggi { get; set; }
        public int GulaTinggi { get; set; }
        public int KolesterolTinggi { get; set; }
    }

    public class ReportLabelCountRow
    {
        public string Label { get; set; } = string.Empty;
        public int Jumlah { get; set; }
    }

    public class ReportFollowUpRow
    {
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime ExaminedAtUtc { get; set; }
        public DateTime FollowUpDate { get; set; }
        public int HariTerlambat { get; set; }
    }

    // ------------------------- Tes urin & alkohol -------------------------

    public class ReportDrugSummary
    {
        public int Total { get; set; }
        public int TesUrin { get; set; }
        public int UrinPositif { get; set; }
        public int TesAlkohol { get; set; }
        public int AlkoholPositif { get; set; }
    }

    public class ReportDrugGroupRow
    {
        public string Nama { get; set; } = string.Empty;   // alasan tes atau department
        public int Total { get; set; }
        public int UrinPositif { get; set; }
        public int AlkoholPositif { get; set; }
    }

    // ------------------------- Halaman laporan -------------------------

    public class ClinicReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public DateTime PrevFrom { get; set; }
        public DateTime PrevTo { get; set; }
        public int Days { get; set; }
        public string? Department { get; set; }
        public List<string> Departments { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public int RangeDayLimit { get; set; }
        public bool RangeWasClamped { get; set; }

        public bool CanVisit { get; set; }
        public bool CanCheckup { get; set; }
        public bool CanDrug { get; set; }

        // Kunjungan & obat (permission medical.visit)
        public ReportSummary Summary { get; set; } = new();
        public ReportSummary Previous { get; set; } = new();
        public List<string> TrendLabels { get; set; } = new();
        public List<int> TrendValues { get; set; } = new();
        public List<ReportDeptRow> ByDepartment { get; set; } = new();
        public List<ReportNameCountRow> ByCategory { get; set; } = new();
        public List<ReportDiseaseRow> TopDiseases { get; set; } = new();
        public List<string> DiseaseMonthLabels { get; set; } = new();
        public List<(string Nama, List<int> Values)> DiseaseMonthSeries { get; set; } = new();
        public List<ReportRepeatVisitorRow> RepeatVisitors { get; set; } = new();
        public List<ReportNameCountRow> RepeatDiseases { get; set; } = new();
        public List<ReportProfileRow> Profile { get; set; } = new();
        public int[,] Heat { get; set; } = new int[7, 24];
        public List<ReportMedicineUseRow> TopMedicines { get; set; } = new();
        public List<ReportStockRow> StockAlerts { get; set; } = new();

        // Pemeriksaan kesehatan (permission medical.checkup)
        public ReportCheckupSummary Checkup { get; set; } = new();
        public List<ReportLabelCountRow> RiskDistribution { get; set; } = new();
        public List<ReportCheckupDeptRow> CheckupByDepartment { get; set; } = new();
        public List<ReportFollowUpRow> OverdueFollowUps { get; set; } = new();

        // Tes urin & alkohol (permission medical.drugtest)
        public ReportDrugSummary Drug { get; set; } = new();
        public List<ReportDrugGroupRow> DrugByReason { get; set; } = new();
        public List<ReportDrugGroupRow> DrugByDepartment { get; set; } = new();
    }
}
