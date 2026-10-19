namespace FitToWorkApp.Models.ViewModels
{
    // Model laporan Fit To Work (baca-saja). Aturan hitung sama dengan dashboard:
    // 1 karyawan dihitung 1x per hari (tanggal lokal site); kalau mengisi ulang, status terakhir yang dipakai.

    public class FtwEmployeeRow
    {
        public int Id { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public int HariWajib { get; set; }            // hari operasional sejak karyawan terdaftar
        public int HariIsi { get; set; }              // hari yang ada pengisian
        public int HariTidakFit { get; set; }         // dari hari terisi, status akhirnya tidak fit
        public DateTime? TerakhirUtc { get; set; }    // pengisian terakhir dalam periode
        public int BelumIsiBerturut { get; set; }     // hari operasional terakhir yang belum diisi (sampai akhir periode)

        public decimal? Persen => HariWajib > 0 ? Math.Round(Math.Min(HariIsi, HariWajib) * 100m / HariWajib, 1) : null;

        public string Kategori =>
            HariWajib == 0 ? "Belum ada data"
            : HariIsi == 0 ? "Tidak pernah"
            : Persen >= 90 ? "Tertib"
            : Persen >= 70 ? "Cukup"
            : "Kurang";
    }

    public class FtwDailyRow
    {
        public DateTime Tanggal { get; set; }
        public int Isi { get; set; }
        public int Fit { get; set; }
        public int TidakFit { get; set; }
        public int Target { get; set; }

        public decimal? Persen => Target > 0 ? Math.Round(Math.Min(Isi, Target) * 100m / Target, 1) : null;
    }

    public class FtwGroupRow
    {
        public string Nama { get; set; } = string.Empty;
        public int Karyawan { get; set; }
        public int HariWajib { get; set; }
        public int HariIsi { get; set; }
        public int HariTidakFit { get; set; }
        public int TidakPernah { get; set; }
        public int Kurang { get; set; }               // kepatuhan di bawah 70% (termasuk yang tidak pernah)

        public decimal? Persen => HariWajib > 0 ? Math.Round(Math.Min(HariIsi, HariWajib) * 100m / HariWajib, 1) : null;
        public decimal? PersenTidakFit => HariIsi > 0 ? Math.Round(HariTidakFit * 100m / HariIsi, 1) : null;
    }

    public class FtwFitRow
    {
        public string Nama { get; set; } = string.Empty;
        public int Fit { get; set; }
        public int TidakFit { get; set; }

        public int Total => Fit + TidakFit;
        public decimal? PersenTidakFit => Total > 0 ? Math.Round(TidakFit * 100m / Total, 1) : null;
    }

    public class FtwNameCountRow
    {
        public string Grup { get; set; } = string.Empty;   // Gejala / Kualitas / Fokus / Obat
        public string Nama { get; set; } = string.Empty;
        public int Jumlah { get; set; }
    }

    public class FtwFollowUpRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string JadwalKerja { get; set; } = string.Empty;
        public string? NomorHp { get; set; }
        public string AreaKerja { get; set; } = string.Empty;
        public DateTime WaktuUtc { get; set; }
        public string GejalaKesehatan { get; set; } = string.Empty;
        public string JamTidur { get; set; } = string.Empty;
        public string KualitasTidur { get; set; } = string.Empty;
        public string KondisiFokus { get; set; } = string.Empty;
        public string KonsumsiObat { get; set; } = string.Empty;
        public DateTime? IsiUlangFitUtc { get; set; }        // pengisian ulang berstatus FIT dalam 24 jam berikutnya
        public int TidakFit7Hari { get; set; }               // jumlah hari tidak fit dalam 7 hari terakhir (termasuk ini)
        public DateTime? KlinikUtc { get; set; }             // kunjungan klinik dalam 48 jam berikutnya (hanya kalau boleh dilihat)
        public string? KlinikRekomendasi { get; set; }
        public int? KlinikHariIstirahat { get; set; }
        public int? TriageId { get; set; }                   // pemeriksaan awal perawat (draft / selesai) untuk kejadian ini
        public string? TriageStatus { get; set; }            // Draft | Selesai
        public int? TriageVisitId { get; set; }

        // Diisi controller
        public string Status { get; set; } = string.Empty;
        public string StatusKode { get; set; } = string.Empty;   // belum | triage | klinik | isiulang
        public bool Prioritas { get; set; }
        public int JamTerbuka { get; set; }                      // jam sejak pengisian (untuk yang belum ada tindak lanjut)
    }

    public class FtwReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Days { get; set; }
        public string? Department { get; set; }
        public List<string> Departments { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public bool RangeWasClamped { get; set; }
        public int RangeDayLimit { get; set; }

        // Ringkasan
        public int KaryawanAktif { get; set; }
        public int HariOperasional { get; set; }
        public int TotalPengisian { get; set; }       // hari-karyawan yang terisi
        public int TotalFit { get; set; }
        public int TotalTidakFit { get; set; }
        public decimal? KepatuhanPersen { get; set; }
        public int JumlahTertib { get; set; }
        public int JumlahCukup { get; set; }
        public int JumlahKurang { get; set; }
        public int JumlahTidakPernah { get; set; }

        public List<FtwDailyRow> Daily { get; set; } = new();
        public List<(string Hari, decimal? Persen)> Weekday { get; set; } = new();
        public List<FtwGroupRow> ByDepartment { get; set; } = new();
        public List<FtwGroupRow> BySection { get; set; } = new();
        public List<FtwEmployeeRow> Employees { get; set; } = new();          // semua (untuk Excel)
        public List<FtwEmployeeRow> LowCompliance { get; set; } = new();      // paling jarang mengisi (ditampilkan)
        public List<FtwEmployeeRow> FrequentUnfit { get; set; } = new();      // sering tidak fit

        public List<FtwFitRow> ByJadwal { get; set; } = new();
        public List<FtwFitRow> ByJamTidur { get; set; } = new();
        public List<FtwNameCountRow> Gejala { get; set; } = new();
        public List<FtwNameCountRow> Kualitas { get; set; } = new();
        public List<FtwNameCountRow> Fokus { get; set; } = new();
        public List<FtwNameCountRow> Obat { get; set; } = new();
    }

    public class FtwFollowUpViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Department { get; set; }
        public string Status { get; set; } = "all";
        public List<string> Departments { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public bool CanSeeClinic { get; set; }
        public bool CanTriage { get; set; }              // boleh memulai pemeriksaan awal (medical.triage / medical.visit)
        public bool CanDoctor { get; set; }              // boleh melanjutkan sebagai dokter (medical.visit)
        public bool TriageAvailable { get; set; }        // tabel pemeriksaan awal sudah ada di database
        public bool Truncated { get; set; }

        public int Kejadian { get; set; }
        public int KaryawanUnik { get; set; }
        public int Belum { get; set; }
        public int BelumPrioritas { get; set; }
        public int Klinik { get; set; }
        public int MenungguDokter { get; set; }
        public int IsiUlang { get; set; }

        public List<FtwFollowUpRow> Rows { get; set; } = new();
    }
}
