namespace FitToWorkApp.Models
{
    public static class OpnameStatuses
    {
        public const string Berjalan = "Berjalan";
        public const string Selesai = "Selesai";
        public const string Batal = "Batal";

        public static string BadgeClass(string s) => s switch
        {
            Berjalan => "bg-primary",
            Selesai => "bg-success",
            _ => "bg-secondary"
        };
    }

    /// <summary>Stok opname untuk satu periode. Bisa diisi parsial (bertahap) sampai semua obat dihitung atau opname ditutup.</summary>
    public class StockOpname
    {
        public int Id { get; set; }
        public string PeriodeLabel { get; set; } = string.Empty;
        public DateTime PeriodeMulai { get; set; }
        public DateTime PeriodeSelesai { get; set; }
        public string Status { get; set; } = OpnameStatuses.Berjalan;
        public string? Catatan { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? LastFilledAt { get; set; }      // UTC: terakhir ada hitungan yang diisi / diubah
        public DateTime? FinishedAt { get; set; }
        public string? FinishedBy { get; set; }

        // Hasil agregasi (bukan kolom tabel)
        public int TotalItem { get; set; }
        public int Dihitung { get; set; }                // sudah ada stok fisik
        public int Diterapkan { get; set; }              // selisih sudah diterapkan ke stok
        public int Menunggu => Dihitung - Diterapkan;     // sudah dihitung, belum diterapkan
        public int BelumDihitung => TotalItem - Dihitung;

        public bool IsOpen => Status == OpnameStatuses.Berjalan;
    }

    public class StockOpnameItem
    {
        public int Id { get; set; }
        public int OpnameId { get; set; }
        public int MedicineId { get; set; }
        public string NamaSnapshot { get; set; } = string.Empty;
        public string SatuanSnapshot { get; set; } = string.Empty;
        public int? StokSistem { get; set; }             // stok sistem saat hitungan disimpan
        public int? StokFisik { get; set; }
        public DateTime? CountedAt { get; set; }         // UTC
        public string? CountedBy { get; set; }
        public string? Catatan { get; set; }
        public DateTime? AppliedAt { get; set; }         // UTC
        public int? AppliedDelta { get; set; }
        public string? AppliedBy { get; set; }

        // Dari JOIN ke Medicines (stok saat ini)
        public int StokSekarang { get; set; }
        public int StokMinimum { get; set; }
        public bool MedicineActive { get; set; }

        public bool IsCounted => StokFisik.HasValue;
        public bool IsApplied => AppliedAt.HasValue;

        /// <summary>Selisih hitungan = fisik - stok sistem saat dihitung. Null kalau belum dihitung.</summary>
        public int? Selisih => StokFisik.HasValue && StokSistem.HasValue ? StokFisik.Value - StokSistem.Value : null;
    }

    /// <summary>Opname terakhir suatu obat (untuk kolom "Opname terakhir" di daftar obat).</summary>
    public class MedicineLastOpname
    {
        public int MedicineId { get; set; }
        public DateTime CountedAt { get; set; }          // UTC
        public string PeriodeLabel { get; set; } = string.Empty;
    }
}
