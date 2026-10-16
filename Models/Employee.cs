namespace FitToWorkApp.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? NomorHp { get; set; }
        public int? DivisionId { get; set; }
        public int? DepartmentId { get; set; }
        public int? SectionId { get; set; }
        public int? PositionId { get; set; }
        public DateTime? TanggalLahir { get; set; }   // password awal Riwayat Saya (ddMMyyyy)
        public DateTime? PortalPasswordChangedAt { get; set; }   // NULL = masih password awal (tanggal lahir). Hash-nya TIDAK dimuat ke model.
        public string? JenisKelamin { get; set; }     // "L" / "P" - acuan nilai normal pemeriksaan kesehatan
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Karyawan mitra kerja (null = karyawan AKP). NIP karyawan mitra = Kode mitra + "-" + MitraNip, mis. GPE-3450.
        public int? MitraId { get; set; }
        public string? MitraNip { get; set; }
        public string? MitraJabatan { get; set; }

        // Terisi lewat JOIN saat query (bukan kolom asli tabel Employees)
        public string? DivisionName { get; set; }
        public string? DepartmentName { get; set; }
        public string? SectionName { get; set; }
        public string? PositionName { get; set; }
        public string? MitraKode { get; set; }
        public string? MitraNama { get; set; }
        public int JumlahKunjungan { get; set; }      // hanya diisi di daftar Karyawan Mitra
        public int JumlahTes { get; set; }            // tes urin & alkohol, hanya diisi di daftar Karyawan Mitra

        public bool IsMitra => MitraId.HasValue;

        /// <summary>Unit kerja untuk snapshot data medis: nama mitra untuk karyawan mitra, department untuk karyawan AKP.</summary>
        public string UnitKerja => IsMitra ? MitraNama ?? "Mitra" : DepartmentName ?? string.Empty;
        public string BagianKerja => IsMitra ? "Mitra Kerja" : SectionName ?? string.Empty;
        public string JabatanKerja => IsMitra ? MitraJabatan ?? string.Empty : PositionName ?? string.Empty;
    }

    /// <summary>Perusahaan mitra kerja (kontraktor / subkon).</summary>
    public class Mitra
    {
        public int Id { get; set; }
        public string Kode { get; set; } = string.Empty;   // 3 huruf kapital
        public string Nama { get; set; } = string.Empty;
        public string? Keterangan { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public int JumlahKaryawan { get; set; }             // hasil query

        public static string BuildId(string kode, string nip) => $"{kode.Trim().ToUpperInvariant()}-{nip.Trim()}";
    }

    /// <summary>Karyawan mana yang diambil saat mencari / menampilkan data.</summary>
    public enum EmployeeScope
    {
        Akp,     // karyawan AKP saja (default: FTW, gym, data karyawan, dashboard)
        Mitra,   // karyawan mitra saja
        All      // keduanya (kunjungan klinik & rekam medis)
    }

    /// <summary>Ringkasan aktivitas satu karyawan untuk halaman Detail Karyawan (waktu dalam UTC).</summary>
    public class EmployeeActivitySummary
    {
        public int FtwTotal { get; set; }
        public int FtwFit { get; set; }
        public DateTime? FtwLastAt { get; set; }
        public int GymTotal { get; set; }
        public DateTime? GymLastAt { get; set; }
        public int CheckupTotal { get; set; }
        public DateTime? CheckupLastAt { get; set; }
        public int VisitTotal { get; set; }
        public DateTime? VisitLastAt { get; set; }
    }

    // // Daftar section sesuai form asli, dipakai untuk dropdown di admin & seeding
    // public static class SectionList
    // {
    //     public static readonly string[] All = new[]
    //     {
    //         "GENERAL MANAGER", "MANAGER", "PRODUCTION", "MINE PROJECT", "GRADE CONTROL",
    //         "STOCKPILE", "PREPARATION", "LABORATORIUM", "SHIPPING", "GEOLOGY",
    //         "MINE PLAN ENGINEERING", "SURVEY", "CONSTRUCTION", "MECHANICAL ELECTRICAL",
    //         "COMPLIANCE", "SAFETY / ERT / HEALTH", "ENVIRONMENT", "FORESTRY",
    //         "HR DEVELOPMENT", "HR OPERATIONAL", "GA", "IT", "EXTERNAL RELATION",
    //         "SECURITY", "FINANCE", "PLANNING & LOGISTIC",
    //         "ENGINEERING - PROJECT DEVELOPMENT",
    //         "NON ENGINEERING - STONE CRUSHER - PROJECT DEVELOPMENT"
    //     };
    // }
}
