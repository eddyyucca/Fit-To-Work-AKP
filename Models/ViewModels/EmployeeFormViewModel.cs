using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    public class EmployeeFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "NIP wajib diisi")]
        [Display(Name = "NIP")]
        public string NIP { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama wajib diisi")]
        public string Nama { get; set; } = string.Empty;

        [Display(Name = "Nomor HP")]
        public string? NomorHp { get; set; }

        [Display(Name = "Division")]
        public int? DivisionId { get; set; }

        [Display(Name = "Department")]
        public int? DepartmentId { get; set; }

        [Required(ErrorMessage = "Section wajib dipilih")]
        [Display(Name = "Section")]
        public int? SectionId { get; set; }

        [Display(Name = "Position / Jabatan")]
        public int? PositionId { get; set; }

        [Display(Name = "Tanggal Lahir")]
        [DataType(DataType.Date)]
        public DateTime? TanggalLahir { get; set; }

        [Display(Name = "Jenis Kelamin")]
        [RegularExpression("^[LP]$", ErrorMessage = "Pilih L atau P")]
        public string? JenisKelamin { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsEdit { get; set; }
    }

    public class HistoryFilterViewModel
    {
        public string? NIP { get; set; }
        public string? Nama { get; set; }
        public string? Section { get; set; }
        public string? KesiapanKerja { get; set; }
        public DateTime? TanggalMulai { get; set; }
        public DateTime? TanggalSelesai { get; set; }

        public List<FitToWorkSubmission> Results { get; set; } = new();
    }


}