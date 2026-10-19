namespace FitToWorkApp.Models
{
    // ======================= Master data =======================

    /// <summary>Master jenis penyakit / diagnosa (dipilih saat kunjungan klinik).</summary>
    public class Disease
    {
        public int Id { get; set; }
        public string? Kode { get; set; }          // opsional, mis. kode ICD-10 "J06.9"
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }        // opsional, nama Inggris asli ICD-10 WHO, mis. "Acute upper respiratory infection, unspecified"
        public string? Kategori { get; set; }      // opsional, mis. "Pernapasan", "Pencernaan"
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Kode) ? Name : $"{Kode} - {Name}";
    }

    /// <summary>Master obat + stok saat ini (Stok selalu diubah lewat MedicineStockMovements).</summary>
    public class Medicine
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Satuan { get; set; } = "tablet";
        public string? Keterangan { get; set; }
        public int Stok { get; set; }
        public int StokMinimum { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        public bool IsLowStock => Stok <= StokMinimum;
    }

    public static class StockMovementTypes
    {
        public const string Masuk = "Masuk";                 // penerimaan obat
        public const string Keluar = "Keluar";               // diberikan saat kunjungan klinik
        public const string Penyesuaian = "Penyesuaian";     // stok opname / koreksi
        public const string Batal = "BatalKunjungan";        // stok kembali karena kunjungan dibatalkan
        public const string Awal = "StokAwal";

        public static string Label(string type) => type switch
        {
            Masuk => "Stok masuk",
            Keluar => "Keluar (kunjungan)",
            Penyesuaian => "Penyesuaian / opname",
            Batal => "Kembali (kunjungan batal)",
            Awal => "Stok awal",
            _ => type
        };
    }

    public class MedicineStockMovement
    {
        public long Id { get; set; }
        public int MedicineId { get; set; }
        public string Type { get; set; } = string.Empty;
        public int Qty { get; set; }                // + masuk, - keluar
        public int StokSetelah { get; set; }
        public int? ClinicVisitId { get; set; }
        public string? Keterangan { get; set; }
        public int? AdminUserId { get; set; }
        public string? AdminName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ======================= Pemeriksaan kesehatan =======================

    public class HealthCheckup
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string DepartmentSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string PositionSnapshot { get; set; } = string.Empty;
        public string? JenisKelamin { get; set; }  // snapshot, dipakai untuk acuan nilai normal

        public DateTime ExaminedAt { get; set; }   // UTC
        public int? Sistolik { get; set; }         // mmHg
        public int? Diastolik { get; set; }        // mmHg
        public decimal? BeratBadan { get; set; }   // kg
        public decimal? TinggiBadan { get; set; }  // cm (opsional, untuk IMT)
        public decimal? LingkarPerut { get; set; } // cm
        public int? Kolesterol { get; set; }       // mg/dL (total)
        public decimal? AsamUrat { get; set; }     // mg/dL
        public int? GulaDarah { get; set; }        // mg/dL
        public string GulaDarahJenis { get; set; } = GulaDarahTypes.Sewaktu;
        public string? Keluhan { get; set; }
        public string? Catatan { get; set; }

        // Temuan & follow up
        public bool AdaTemuan { get; set; }
        public string? TemuanKeterangan { get; set; }
        public DateTime? FollowUpDate { get; set; }       // tanggal lokal site (kolom DATE)
        public DateTime? FollowUpDoneAt { get; set; }     // UTC, null = belum selesai
        public string? FollowUpDoneBy { get; set; }
        public string? FollowUpHasil { get; set; }

        public int? ExaminerAdminId { get; set; }
        public string ExaminerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        // Format baru: parameter custom + skor risiko (dbo.HealthCheckupValues). False = data lama (kolom tetap di atas).
        public bool UsesParameters { get; set; }
        public int? TotalSkor { get; set; }
        public int? SkorParamTerisi { get; set; }     // berapa parameter berpoin yang terisi
        public int? SkorParamTotal { get; set; }      // dari berapa parameter berpoin yang aktif saat itu
        public string? RisikoLabel { get; set; }
        public string? RisikoColor { get; set; }
        public List<HealthCheckupValue> Values { get; set; } = new();

        // JOIN
        public string? NIP { get; set; }
        public int AttachmentCount { get; set; }

        public bool SkorLengkap => SkorParamTerisi.HasValue && SkorParamTotal.HasValue && SkorParamTerisi >= SkorParamTotal;

        public bool FollowUpOpen => AdaTemuan && FollowUpDoneAt is null;
        public bool IsFollowUpOverdue(DateTime today) => FollowUpOpen && FollowUpDate.HasValue && FollowUpDate.Value.Date < today.Date;

        /// <summary>Status follow up untuk tampilan: null (tidak ada temuan), Selesai, Terlambat, HariIni, Terjadwal.</summary>
        public string? FollowUpStatus(DateTime today) =>
            !AdaTemuan ? null
            : FollowUpDoneAt.HasValue ? FollowUpStatuses.Selesai
            : FollowUpDate is null ? FollowUpStatuses.Terjadwal
            : FollowUpDate.Value.Date < today.Date ? FollowUpStatuses.Terlambat
            : FollowUpDate.Value.Date == today.Date ? FollowUpStatuses.HariIni
            : FollowUpStatuses.Terjadwal;

        public decimal? Imt => TinggiBadan is > 0 && BeratBadan is > 0
            ? Math.Round(BeratBadan.Value / ((TinggiBadan.Value / 100m) * (TinggiBadan.Value / 100m)), 1)
            : null;

        public string? TekananDarah => Sistolik.HasValue && Diastolik.HasValue ? $"{Sistolik}/{Diastolik}" : null;
    }

    public static class FollowUpStatuses
    {
        public const string Terjadwal = "Terjadwal";
        public const string HariIni = "HariIni";
        public const string Terlambat = "Terlambat";
        public const string Selesai = "Selesai";

        public static string Label(string? s) => s switch
        {
            HariIni => "Hari ini",
            Terlambat => "Terlambat",
            Selesai => "Selesai",
            Terjadwal => "Terjadwal",
            _ => "-"
        };

        public static string BadgeClass(string? s) => s switch
        {
            HariIni => "bg-warning text-dark",
            Terlambat => "bg-danger",
            Selesai => "bg-success",
            Terjadwal => "bg-info text-dark",
            _ => "bg-secondary"
        };
    }

    public static class GulaDarahTypes
    {
        public const string Sewaktu = "Sewaktu";
        public const string Puasa = "Puasa";
        public const string DuaJamPP = "2JamPP";
        public static readonly string[] All = { Sewaktu, Puasa, DuaJamPP };
        public static string Label(string t) => t switch { Puasa => "Puasa", DuaJamPP => "2 jam PP", _ => "Sewaktu" };
    }

    // ======================= Kunjungan klinik =======================

    public static class RecommendationTypes
    {
        public const string KembaliBekerja = "KembaliBekerja";
        public const string KerjaTerbatas = "KerjaTerbatas";
        public const string Istirahat = "Istirahat";
        public const string Rujuk = "Rujuk";
        public static readonly string[] All = { KembaliBekerja, KerjaTerbatas, Istirahat, Rujuk };

        public static string Label(string t, int? hari = null, DateTime? dari = null, DateTime? sampai = null) => t switch
        {
            KembaliBekerja => "Kembali bekerja",
            KerjaTerbatas => "Kerja terbatas",
            Istirahat when dari.HasValue && sampai.HasValue => $"Istirahat {Periode(dari.Value, sampai.Value)} ({JumlahHari(dari.Value, sampai.Value)} hari)",
            Istirahat => hari.HasValue ? $"Istirahat {hari} hari" : "Istirahat",
            Rujuk => "Rujuk ke RS/Faskes",
            _ => t
        };

        /// <summary>Jumlah hari istirahat, tanggal mulai & selesai ikut dihitung (29/09 - 01/10 = 3 hari).</summary>
        public static int JumlahHari(DateTime dari, DateTime sampai) => (sampai.Date - dari.Date).Days + 1;

        public static string Periode(DateTime dari, DateTime sampai) =>
            dari.Date == sampai.Date ? $"{dari:dd/MM/yyyy}" : $"{dari:dd/MM/yyyy} - {sampai:dd/MM/yyyy}";

        public static string BadgeClass(string t) => t switch
        {
            KembaliBekerja => "bg-success",
            KerjaTerbatas => "bg-warning text-dark",
            Istirahat => "bg-info text-dark",
            Rujuk => "bg-danger",
            _ => "bg-secondary"
        };
    }

    public class ClinicVisit
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string DepartmentSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string PositionSnapshot { get; set; } = string.Empty;

        public DateTime VisitAt { get; set; }      // UTC
        public string Keluhan { get; set; } = string.Empty;
        public string? Pemeriksaan { get; set; }   // hasil pemeriksaan singkat (opsional: tanda vital, dll)
        public string Rekomendasi { get; set; } = RecommendationTypes.KembaliBekerja;
        public int? HariIstirahat { get; set; }           // dihitung dari tanggal (data lama: diisi manual)
        public DateTime? IstirahatDari { get; set; }      // tanggal lokal site (kolom DATE)
        public DateTime? IstirahatSampai { get; set; }
        public string? CatatanRekomendasi { get; set; }

        public int? ExaminerAdminId { get; set; }
        public string ExaminerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public bool IsCancelled { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelledBy { get; set; }

        // JOIN / detail
        public string? NIP { get; set; }
        public string? DiagnosaText { get; set; }  // gabungan nama penyakit (untuk tabel)
        public List<Disease> Diseases { get; set; } = new();
        public List<ClinicVisitMedicine> Medicines { get; set; } = new();

        public string RekomendasiLabel => RecommendationTypes.Label(Rekomendasi, HariIstirahat, IstirahatDari, IstirahatSampai);

        /// <summary>mis. "29/09/2026 - 01/10/2026" (null kalau bukan istirahat / data lama tanpa tanggal).</summary>
        public string? PeriodeIstirahat => Rekomendasi == RecommendationTypes.Istirahat && IstirahatDari.HasValue && IstirahatSampai.HasValue
            ? RecommendationTypes.Periode(IstirahatDari.Value, IstirahatSampai.Value)
            : null;
    }

    public class ClinicVisitMedicine
    {
        public int Id { get; set; }
        public int ClinicVisitId { get; set; }
        public int MedicineId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string SatuanSnapshot { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string? AturanPakai { get; set; }   // mis. "3x1 sesudah makan"
    }

    // ======================= Hasil tes (Negatif/Positif) =======================

    public static class TestResults
    {
        public const string Negatif = "Negatif";
        public const string Positif = "Positif";
        public static readonly string[] All = { Negatif, Positif };
        public static string BadgeClass(string? r) => r == Positif ? "bg-danger" : r == Negatif ? "bg-success" : "bg-secondary";
    }

    // ======================= Lampiran (upload hasil pemeriksaan) =======================

    public static class AttachmentEntityTypes
    {
        public const string Checkup = "Checkup";
        public const string DrugTest = "DrugTest";
    }

    public class MedicalAttachment
    {
        public int Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? UploadedBy { get; set; }

        public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        public string SizeLabel => SizeBytes >= 1024 * 1024 ? $"{SizeBytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, SizeBytes / 1024)} KB";
    }

    /// <summary>Model untuk partial _Attachments (daftar lampiran + form upload).</summary>
    public class AttachmentListViewModel
    {
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public List<MedicalAttachment> Items { get; set; } = new();
        public bool CanEdit { get; set; }        // tampilkan tombol upload & hapus (admin)
        public bool PortalMode { get; set; }     // link file lewat /Portal (karyawan melihat miliknya sendiri)
    }

    // ======================= Tes Urin & Alkohol =======================

    public static class DrugTestReasons
    {
        public const string Rutin = "Rutin";
        public const string Acak = "Acak";
        public const string PraKerja = "PraKerja";
        public const string PascaInsiden = "PascaInsiden";
        public const string Kecurigaan = "Kecurigaan";
        public const string Lainnya = "Lainnya";
        public static readonly string[] All = { Rutin, Acak, PraKerja, PascaInsiden, Kecurigaan, Lainnya };

        public static string Label(string? r) => r switch
        {
            Rutin => "Rutin / berkala",
            Acak => "Acak (random)",
            PraKerja => "Pra-kerja / MCU",
            PascaInsiden => "Pasca insiden",
            Kecurigaan => "Atas dasar kecurigaan",
            Lainnya => "Lainnya",
            _ => r ?? "-"
        };
    }

    public class DrugAlcoholTest
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string DepartmentSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string PositionSnapshot { get; set; } = string.Empty;

        public DateTime TestedAt { get; set; }        // UTC
        public string Alasan { get; set; } = DrugTestReasons.Rutin;
        public bool TesUrin { get; set; }
        public string? UrinHasil { get; set; }
        public bool TesAlkohol { get; set; }
        public decimal? AlkoholKadar { get; set; }
        public decimal? AlkoholBatas { get; set; }
        public string? AlkoholHasil { get; set; }
        public string? Catatan { get; set; }

        public int? ExaminerAdminId { get; set; }
        public string ExaminerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        // JOIN / detail
        public string? NIP { get; set; }
        public int? MitraId { get; set; }             // terisi = karyawan mitra
        public int AttachmentCount { get; set; }
        public List<DrugTestUrineItem> UrineItems { get; set; } = new();

        public bool IsMitra => MitraId.HasValue;

        public bool IsPositive => UrinHasil == TestResults.Positif || AlkoholHasil == TestResults.Positif;
        public string HasilAkhir => IsPositive ? TestResults.Positif : TestResults.Negatif;
    }

    public class DrugTestUrineItem
    {
        public int Id { get; set; }
        public int TestId { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string Hasil { get; set; } = TestResults.Negatif;
    }
}
