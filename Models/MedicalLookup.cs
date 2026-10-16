namespace FitToWorkApp.Models
{
    /// <summary>Jenis master pilihan sederhana untuk modul klinik.</summary>
    public enum MedicalLookupKind
    {
        DiseaseCategory,   // Kategori penyakit  -> dbo.DiseaseCategories, dipakai Diseases.Kategori
        MedicineUnit,      // Satuan obat        -> dbo.MedicineUnits,     dipakai Medicines.Satuan
        UrineParameter     // Parameter tes urin -> dbo.UrineTestParameters, dipakai DrugAlcoholTestUrineItems.ParameterName (snapshot)
    }

    /// <summary>Satu baris master Kategori Penyakit / Satuan Obat.</summary>
    public class MedicalLookup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        /// <summary>Jumlah jenis penyakit / obat yang memakai nilai ini (hasil query).</summary>
        public int UsageCount { get; set; }
    }
}
