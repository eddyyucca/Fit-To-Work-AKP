using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Form mulai aktivitas kebugaran (pilih jenis aktivitas, berat badan kalau diwajibkan).</summary>
    public class GymFormViewModel
    {
        // Hanya untuk tampilan (read-only). Saat submit, data diri selalu diambil ulang dari database.
        public string Nama { get; set; } = string.Empty;
        public string NIP { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;

        /// <summary>Pilihan jenis aktivitas aktif (untuk tampilan).</summary>
        public List<ActivityType> Types { get; set; } = new();

        public const decimal MinBerat = 30m;
        public const decimal MaxBerat = 200m;

        [Required(ErrorMessage = "Pilih jenis aktivitas")]
        [Display(Name = "Jenis Aktivitas")]
        public int? ActivityTypeId { get; set; }

        /// <summary>Wajib hanya untuk jenis aktivitas dengan WajibBerat (dicek di controller).</summary>
        [Range(typeof(decimal), "30", "200", ErrorMessage = "Berat badan harus antara 30 dan 200 kg")]
        [Display(Name = "Berat Badan (kg)")]
        public decimal? BeratBadan { get; set; }
    }
}
