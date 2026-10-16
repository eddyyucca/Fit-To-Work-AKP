using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    public class AdminUserCreateViewModel
    {
        [Required(ErrorMessage = "Username wajib diisi")]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama lengkap wajib diisi")]
        [Display(Name = "Nama Lengkap")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role wajib dipilih")]
        [Display(Name = "Role")]
        public int? RoleId { get; set; }

        [Required(ErrorMessage = "Password wajib diisi")]
        [MinLength(6, ErrorMessage = "Password minimal 6 karakter")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Konfirmasi password wajib diisi")]
        [DataType(DataType.Password)]
        [Display(Name = "Konfirmasi Password")]
        [Compare(nameof(Password), ErrorMessage = "Konfirmasi password tidak sama")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>Edit user admin: nama lengkap & tautan karyawan (username, role & password punya jalur sendiri).</summary>
    public class AdminUserEditViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;   // hanya ditampilkan
        public string? RoleName { get; set; }                  // hanya ditampilkan
        public bool IsActive { get; set; }                     // hanya ditampilkan
        public bool IsSelf { get; set; }

        [Required(ErrorMessage = "Nama lengkap wajib diisi")]
        [StringLength(150, ErrorMessage = "Nama lengkap maksimal 150 karakter")]
        [Display(Name = "Nama Lengkap")]
        public string FullName { get; set; } = string.Empty;

        /// <summary>Karyawan AKP yang terhubung (kosong = tidak terhubung).</summary>
        public int? EmployeeId { get; set; }
        public string? EmployeeLabel { get; set; }             // "NIP - Nama", untuk tampilan
    }

    public class AdminChangePasswordViewModel
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        // Diisi hanya kalau admin sedang mengganti password miliknya sendiri,
        // supaya orang lain yang kebetulan pakai sesi terbuka tidak bisa ganti password tanpa tahu password lama.
        public bool IsSelf { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password Saat Ini")]
        public string? CurrentPassword { get; set; }

        [Required(ErrorMessage = "Password baru wajib diisi")]
        [MinLength(6, ErrorMessage = "Password minimal 6 karakter")]
        [DataType(DataType.Password)]
        [Display(Name = "Password Baru")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Konfirmasi password wajib diisi")]
        [DataType(DataType.Password)]
        [Display(Name = "Konfirmasi Password Baru")]
        [Compare(nameof(NewPassword), ErrorMessage = "Konfirmasi password tidak sama")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
