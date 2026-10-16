namespace FitToWorkApp.Models
{
    /// <summary>Satu baris log akses panel admin (login, logout, akses ditolak, buka halaman, export, perubahan data).</summary>
    public class AccessLog
    {
        public long Id { get; set; }
        public DateTime CreatedAt { get; set; }          // UTC
        public int? AdminUserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;   // lihat AccessLogEvents
        public string Activity { get; set; } = string.Empty;    // deskripsi yang mudah dibaca
        public string? HttpMethod { get; set; }
        public string? Path { get; set; }
        public int? StatusCode { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }

    public static class AccessLogEvents
    {
        public const string Login = "Login";
        public const string LoginFailed = "LoginGagal";
        public const string Logout = "Logout";
        public const string AccessDenied = "AksesDitolak";
        public const string View = "Lihat";
        public const string Export = "Export";
        public const string Change = "Ubah";
        public const string PortalLogin = "PortalLogin";
        public const string PortalLoginFailed = "PortalLoginGagal";
        public const string PortalPasswordChanged = "PortalGantiPassword";
        public const string Reset = "ResetData";

        public static readonly string[] All = { Login, LoginFailed, Logout, AccessDenied, View, Export, Change, PortalLogin, PortalLoginFailed, PortalPasswordChanged, Reset };

        public static string Label(string eventType) => eventType switch
        {
            Login => "Login",
            LoginFailed => "Login gagal",
            Logout => "Logout",
            AccessDenied => "Akses ditolak",
            View => "Lihat",
            Export => "Export",
            Change => "Ubah data",
            PortalLogin => "Login Riwayat Saya",
            PortalLoginFailed => "Login Riwayat Saya gagal",
            PortalPasswordChanged => "Ganti password Riwayat Saya",
            Reset => "Reset data",
            _ => eventType
        };
    }
}
