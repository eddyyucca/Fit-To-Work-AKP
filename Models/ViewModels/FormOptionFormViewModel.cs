using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    public class FormOptionFormViewModel
    {
        public int Id { get; set; }

        [Required]
        public string GroupKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Teks pilihan wajib diisi")]
        [Display(Name = "Teks Pilihan")]
        public string OptionValue { get; set; } = string.Empty;

        [Display(Name = "Urutan Tampil")]
        public int SortOrder { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }
}
