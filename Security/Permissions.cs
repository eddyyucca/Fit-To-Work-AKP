using Microsoft.AspNetCore.Authorization;

namespace FitToWorkApp.Security
{
    /// <summary>
    /// Daftar hak akses (per menu) panel admin. Key disimpan di tabel dbo.RolePermissions.
    /// Menambah menu baru = tambah konstanta + entri di <see cref="All"/>, lalu pasang [HasPermission] di controller.
    /// JANGAN ganti string key yang sudah dipakai (data di database bergantung padanya).
    /// </summary>
    public static class Permissions
    {
        public const string FtwDashboard = "ftw.dashboard";
        public const string FtwHistory = "ftw.history";
        public const string FtwSettings = "ftw.settings";
        public const string GymHistory = "gym.history";          // key lama: Riwayat Aktivitas Kebugaran
        public const string FitnessTypes = "fitness.types";
        public const string MasterEmployees = "master.employees";
        public const string MasterOrganization = "master.organization";
        public const string MasterFormOptions = "master.formoptions";
        public const string MasterLocations = "master.locations";
        public const string AdminUsers = "admin.users";
        public const string AdminAccessLog = "admin.accesslog";
        public const string MedicalCheckup = "medical.checkup";
        public const string MedicalVisit = "medical.visit";
        public const string MedicalMaster = "medical.master";
        public const string MedicalDrugTest = "medical.drugtest";

        public record PermissionInfo(string Key, string Group, string Label, string Description);

        public static readonly IReadOnlyList<PermissionInfo> All = new[]
        {
            new PermissionInfo(FtwDashboard, "Fit To Work", "Dashboard", "Dashboard realtime & export PDF"),
            new PermissionInfo(FtwHistory, "Fit To Work", "Riwayat Pengisian", "Lihat detail pengisian & download Excel"),
            new PermissionInfo(FtwSettings, "Fit To Work", "Pengaturan Pengisian", "Jeda / jam reset pengisian berikutnya, tombol Isi Ulang & lama layar \"sudah mengisi\""),
            new PermissionInfo(GymHistory, "Aktivitas Kebugaran", "Riwayat Aktivitas", "Riwayat gym, lari, hiking, dll: durasi, jarak & rute GPS, berat badan, lokasi; dashboard aktivitas"),
            new PermissionInfo(FitnessTypes, "Aktivitas Kebugaran", "Jenis Aktivitas", "Tambah/ubah jenis aktivitas: GPS, wajib berat badan, batas jam, kunci lokasi"),
            new PermissionInfo(MedicalCheckup, "Klinik & Kesehatan", "Pemeriksaan Kesehatan", "Input & lihat hasil pemkes, skor risiko, upload hasil & Follow Up Pemkes - data medis"),
            new PermissionInfo(MedicalVisit, "Klinik & Kesehatan", "Kunjungan Klinik", "Catat kunjungan, diagnosa, obat & rekomendasi, serta kelola Karyawan Mitra - data medis"),
            new PermissionInfo(MedicalDrugTest, "Klinik & Kesehatan", "Tes Urin & Alkohol", "Input & lihat hasil tes urin (narkoba) & tes alkohol karyawan AKP & mitra beserta lampirannya - data medis"),
            new PermissionInfo(MedicalMaster, "Klinik & Kesehatan", "Obat, Stok & Master Klinik", "Obat & stok (termasuk import Excel), jenis & kategori penyakit, satuan obat, parameter tes urin, parameter & skor pemkes, batas alkohol & follow up, Data Mitra"),
            new PermissionInfo(MasterEmployees, "Master Data", "Data Karyawan", "Tambah/ubah/nonaktifkan & import karyawan"),
            new PermissionInfo(MasterOrganization, "Master Data", "Data Organisasi", "Division, Department, Section, Jabatan"),
            new PermissionInfo(MasterFormOptions, "Master Data", "Master Data Form", "Pilihan jawaban form Fit To Work"),
            new PermissionInfo(MasterLocations, "Master Data", "Lokasi Site", "Titik lokasi, radius & Wi-Fi site untuk kunci lokasi pengisian Fit To Work dan Aktivitas Kebugaran"),
            new PermissionInfo(AdminUsers, "Pengaturan", "User Admin & Role", "Kelola akun admin dan hak akses role"),
            new PermissionInfo(AdminAccessLog, "Pengaturan", "Log Akses", "Lihat riwayat login & aktivitas admin")
        };

        public static readonly IReadOnlySet<string> AllKeys = All.Select(p => p.Key).ToHashSet();

        /// <summary>Urutan halaman tujuan setelah login (halaman pertama yang boleh diakses).</summary>
        public static readonly IReadOnlyList<(string Permission, string Controller, string Action)> LandingOrder = new[]
        {
            (FtwDashboard, "Admin", "Dashboard"),
            (FtwHistory, "History", "Index"),
            (GymHistory, "GymHistory", "Index"),
            (FitnessTypes, "ActivityTypes", "Index"),
            (MedicalVisit, "ClinicVisits", "Index"),
            (MedicalCheckup, "Checkups", "Index"),
            (MedicalDrugTest, "DrugTests", "Index"),
            (MedicalMaster, "Medicines", "Index"),
            (MasterEmployees, "Employees", "Index"),
            (MasterOrganization, "Organization", "Index"),
            (MasterFormOptions, "FormOptions", "Index"),
            (MasterLocations, "SiteLocations", "Index"),
            (FtwSettings, "FtwSettings", "Index"),
            (AdminUsers, "Users", "Index"),
            (AdminAccessLog, "AccessLogs", "Index")
        };
    }

    /// <summary>
    /// Wajib admin DAN punya minimal salah satu permission yang disebut.
    /// Contoh: [HasPermission(Permissions.FtwHistory)] atau [HasPermission(Permissions.FtwHistory, Permissions.FtwDashboard)].
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class HasPermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "Perm:";

        public HasPermissionAttribute(params string[] permissions)
        {
            Policy = PolicyPrefix + string.Join("|", permissions);
        }
    }
}
