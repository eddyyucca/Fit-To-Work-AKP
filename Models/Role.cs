namespace FitToWorkApp.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        // Super Admin selalu punya semua hak akses (tidak dicek per permission) dan tidak bisa dihapus.
        public bool IsSuperAdmin { get; set; }
        public DateTime CreatedAt { get; set; }

        // Terisi saat listing (bukan kolom tabel)
        public int UserCount { get; set; }
        public List<string> Permissions { get; set; } = new();
    }
}
