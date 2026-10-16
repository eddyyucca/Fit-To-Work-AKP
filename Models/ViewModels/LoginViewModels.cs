using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    public class EmployeeLoginViewModel
    {
        [Required(ErrorMessage = "NIP wajib diisi")]
        [Display(Name = "NIP / Nomor Induk Karyawan")]
        public string NIP { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public class AdminLoginViewModel
    {
        [Required(ErrorMessage = "Username wajib diisi")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password wajib diisi")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }
}
