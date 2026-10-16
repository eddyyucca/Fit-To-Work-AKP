using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{

    public class DivisionFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama division wajib diisi")]
        [Display(Name = "Nama Division")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Urutan Tampil")]
        public int SortOrder { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }

    public class DepartmentFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama department wajib diisi")]
        [Display(Name = "Nama Department")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Urutan Tampil")]
        public int SortOrder { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }

    public class SectionFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Department wajib dipilih")]
        [Display(Name = "Department")]
        public int DepartmentId { get; set; }

        [Required(ErrorMessage = "Nama section wajib diisi")]
        [Display(Name = "Nama Section")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Urutan Tampil")]
        public int SortOrder { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }

    public class PositionFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama position wajib diisi")]
        [Display(Name = "Nama Position / Jabatan")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Urutan Tampil")]
        public int SortOrder { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }
}