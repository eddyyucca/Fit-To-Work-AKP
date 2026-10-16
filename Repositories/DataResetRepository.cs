using Dapper;
using FitToWorkApp.Data;

namespace FitToWorkApp.Repositories
{
    /// <summary>Satu modul yang bisa dikosongkan lewat menu Pengaturan &gt; Reset Data.</summary>
    public record ResetModule(
        string Key,
        string Group,
        string Label,
        string Description,
        string CountSql,
        string[] Requires,          // modul lain yang WAJIB ikut direset (karena datanya bergantung)
        bool IsMaster = false);

    public static class ResetModules
    {
        public const string Ftw = "ftw";
        public const string Gym = "gym";
        public const string Checkup = "checkup";
        public const string Clinic = "clinic";
        public const string DrugTest = "drugtest";
        public const string Stock = "stock";
        public const string AccessLog = "accesslog";
        public const string Employees = "employees";
        public const string Medicines = "medicines";
        public const string Diseases = "diseases";
        public const string MitraEmployees = "mitraemployees";
        public const string Mitras = "mitras";

        public static readonly IReadOnlyList<ResetModule> All = new[]
        {
            new ResetModule(Ftw, "Data transaksi", "Riwayat Fit To Work", "Semua pengisian form Fit To Work",
                "SELECT COUNT(1) FROM dbo.FitToWorkSubmissions", Array.Empty<string>()),
            new ResetModule(Gym, "Data transaksi", "Riwayat Aktivitas Kebugaran", "Semua sesi gym/lari/olahraga lain, berat badan & rute GPS (jenis aktivitas tidak ikut)",
                "SELECT COUNT(1) FROM dbo.GymAttendances", Array.Empty<string>()),
            new ResetModule(Checkup, "Data transaksi", "Pemeriksaan Kesehatan", "Semua hasil pemeriksaan (tensi, kolesterol, dll.), follow up & lampirannya",
                "SELECT COUNT(1) FROM dbo.HealthCheckups", Array.Empty<string>()),
            new ResetModule(Clinic, "Data transaksi", "Kunjungan Klinik", "Kunjungan, diagnosa & obat yang diberikan. Stok obat saat ini TIDAK berubah.",
                "SELECT COUNT(1) FROM dbo.ClinicVisits", Array.Empty<string>()),
            new ResetModule(DrugTest, "Data transaksi", "Tes Urin & Alkohol", "Semua hasil tes urin & alkohol beserta lampirannya",
                "SELECT COUNT(1) FROM dbo.DrugAlcoholTests", Array.Empty<string>()),
            new ResetModule(Stock, "Data transaksi", "Stok & Kartu Stok Obat", "Semua riwayat kartu stok dihapus dan stok semua obat menjadi 0",
                "SELECT COUNT(1) FROM dbo.MedicineStockMovements", Array.Empty<string>()),
            new ResetModule(AccessLog, "Data transaksi", "Log Akses", "Riwayat login & aktivitas admin (catatan reset ini tetap disimpan)",
                "SELECT COUNT(1) FROM dbo.AccessLogs", Array.Empty<string>()),

            new ResetModule(Employees, "Master data", "Data Karyawan AKP", "Semua karyawan AKP (karyawan mitra tidak ikut). Riwayat FTW, aktivitas kebugaran, pemeriksaan, SEMUA kunjungan klinik & SEMUA tes urin/alkohol (termasuk karyawan mitra) ikut terhapus.",
                "SELECT COUNT(1) FROM dbo.Employees WHERE MitraId IS NULL", new[] { Ftw, Gym, Checkup, Clinic, DrugTest }, IsMaster: true),
            new ResetModule(MitraEmployees, "Master data", "Karyawan Mitra", "Semua karyawan mitra. SEMUA kunjungan klinik & SEMUA tes urin/alkohol (termasuk karyawan AKP) ikut terhapus.",
                "SELECT COUNT(1) FROM dbo.Employees WHERE MitraId IS NOT NULL", new[] { Clinic, DrugTest }, IsMaster: true),
            new ResetModule(Mitras, "Master data", "Data Mitra", "Semua perusahaan mitra. Karyawan mitra, kunjungan klinik & tes urin/alkohol ikut terhapus.",
                "SELECT COUNT(1) FROM dbo.Mitras", new[] { MitraEmployees }, IsMaster: true),
            new ResetModule(Medicines, "Master data", "Master Obat", "Semua data obat. Kunjungan klinik & kartu stok ikut terhapus.",
                "SELECT COUNT(1) FROM dbo.Medicines", new[] { Clinic, Stock }, IsMaster: true),
            new ResetModule(Diseases, "Master data", "Master Jenis Penyakit", "Semua jenis penyakit. Kunjungan klinik ikut terhapus.",
                "SELECT COUNT(1) FROM dbo.Diseases", new[] { Clinic }, IsMaster: true)
        };

        public static ResetModule? Find(string key) => All.FirstOrDefault(m => m.Key == key);

        /// <summary>Tambahkan otomatis modul yang wajib ikut (berulang sampai lengkap).</summary>
        public static HashSet<string> WithDependencies(IEnumerable<string> keys)
        {
            var result = keys.Where(k => Find(k) is not null).ToHashSet();
            bool added;
            do
            {
                added = false;
                foreach (var key in result.ToList())
                    foreach (var req in Find(key)!.Requires)
                        added |= result.Add(req);
            } while (added);
            return result;
        }
    }

    public interface IDataResetRepository
    {
        Task<Dictionary<string, int>> CountAsync();

        /// <summary>Kosongkan modul terpilih dalam 1 transaksi. Mengembalikan jumlah baris terhapus per modul.</summary>
        Task<Dictionary<string, int>> ResetAsync(IReadOnlySet<string> modules);
    }

    public class DataResetRepository : IDataResetRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DataResetRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Dictionary<string, int>> CountAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = string.Join(";\n", ResetModules.All.Select(m => m.CountSql));
            using var multi = await conn.QueryMultipleAsync(sql);
            var result = new Dictionary<string, int>();
            foreach (var m in ResetModules.All)
                result[m.Key] = await multi.ReadSingleAsync<int>();
            return result;
        }

        public async Task<Dictionary<string, int>> ResetAsync(IReadOnlySet<string> modules)
        {
            // Pengaman terakhir: dependensi selalu dilengkapi di sini juga, bukan hanya di controller/JS
            var m = ResetModules.WithDependencies(modules);
            var deleted = new Dictionary<string, int>();

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            Task<int> Exec(string sql) => conn.ExecuteAsync(sql, transaction: tx, commandTimeout: 600);

            // Urutan penting: anak dulu, baru induk (foreign key)
            if (m.Contains(ResetModules.Clinic))
            {
                // Kartu stok tetap ada (kecuali modul stok ikut dipilih), hanya tautannya ke kunjungan dilepas
                await Exec("UPDATE dbo.MedicineStockMovements SET ClinicVisitId = NULL WHERE ClinicVisitId IS NOT NULL");
                await Exec("DELETE FROM dbo.ClinicVisitMedicines");
                await Exec("DELETE FROM dbo.ClinicVisitDiseases");
                deleted[ResetModules.Clinic] = await Exec("DELETE FROM dbo.ClinicVisits");
            }

            if (m.Contains(ResetModules.Checkup))
            {
                // File lampiran di disk dibersihkan setelah commit (lihat DataResetController)
                await Exec("DELETE FROM dbo.MedicalAttachments WHERE EntityType = 'Checkup'");
                deleted[ResetModules.Checkup] = await Exec("DELETE FROM dbo.HealthCheckups");
            }

            if (m.Contains(ResetModules.DrugTest))
            {
                await Exec("DELETE FROM dbo.MedicalAttachments WHERE EntityType = 'DrugTest'");
                await Exec("DELETE FROM dbo.DrugAlcoholTestUrineItems");
                deleted[ResetModules.DrugTest] = await Exec("DELETE FROM dbo.DrugAlcoholTests");
            }

            if (m.Contains(ResetModules.Ftw))
                deleted[ResetModules.Ftw] = await Exec("DELETE FROM dbo.FitToWorkSubmissions");

            if (m.Contains(ResetModules.Gym))
                deleted[ResetModules.Gym] = await Exec("DELETE FROM dbo.GymAttendances");

            if (m.Contains(ResetModules.Stock))
            {
                deleted[ResetModules.Stock] = await Exec("DELETE FROM dbo.MedicineStockMovements");
                await Exec("UPDATE dbo.Medicines SET Stok = 0");
            }

            if (m.Contains(ResetModules.Medicines))
                deleted[ResetModules.Medicines] = await Exec("DELETE FROM dbo.Medicines");

            if (m.Contains(ResetModules.Diseases))
                deleted[ResetModules.Diseases] = await Exec("DELETE FROM dbo.Diseases");

            if (m.Contains(ResetModules.Employees))
            {
                // Karyawan AKP saja. Akun admin tetap ada, hanya tautannya ke data karyawan dilepas
                await Exec(@"UPDATE dbo.AdminUsers SET EmployeeId = NULL
                             WHERE EmployeeId IN (SELECT Id FROM dbo.Employees WHERE MitraId IS NULL)");
                deleted[ResetModules.Employees] = await Exec("DELETE FROM dbo.Employees WHERE MitraId IS NULL");
            }

            if (m.Contains(ResetModules.MitraEmployees))
            {
                await Exec(@"UPDATE dbo.AdminUsers SET EmployeeId = NULL
                             WHERE EmployeeId IN (SELECT Id FROM dbo.Employees WHERE MitraId IS NOT NULL)");
                deleted[ResetModules.MitraEmployees] = await Exec("DELETE FROM dbo.Employees WHERE MitraId IS NOT NULL");
            }

            // Mitra dihapus setelah karyawannya (foreign key Employees.MitraId)
            if (m.Contains(ResetModules.Mitras))
                deleted[ResetModules.Mitras] = await Exec("DELETE FROM dbo.Mitras");

            if (m.Contains(ResetModules.AccessLog))
                deleted[ResetModules.AccessLog] = await Exec("DELETE FROM dbo.AccessLogs");

            tx.Commit();
            return deleted;
        }
    }
}
