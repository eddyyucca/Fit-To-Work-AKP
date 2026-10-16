namespace FitToWorkApp.Models
{
    public class AdminUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int? EmployeeId { get; set; }
        public int? RoleId { get; set; }
        public bool IsActive { get; set; } = true;   // false = tidak bisa login
        public DateTime CreatedAt { get; set; }

        // Terisi lewat JOIN ke dbo.Roles (bukan kolom tabel AdminUsers)
        public string? RoleName { get; set; }
        public bool IsSuperAdmin { get; set; }

        // Terisi lewat JOIN ke dbo.Employees (karyawan yang terhubung dengan akun ini)
        public string? EmployeeNip { get; set; }
        public string? EmployeeNama { get; set; }
    }
}
