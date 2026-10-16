using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Parameter untuk ambil data karyawan per halaman (server-side DataTables).</summary>
    public class EmployeePageQuery
    {
        public string? Search { get; set; }
        public bool? IsActive { get; set; }          // null = semua, true = aktif, false = nonaktif
        public int SortColumn { get; set; } = 1;     // index kolom tabel (lihat SortColumns)
        public bool SortDesc { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public class EmployeePageResult
    {
        public int Total { get; set; }               // total semua karyawan (tanpa filter)
        public int Filtered { get; set; }            // total setelah filter pencarian/status
        public List<Employee> Rows { get; set; } = new();
    }

    public interface IEmployeeRepository
    {
        Task<Employee?> GetByIdAsync(int id);
        Task<EmployeeActivitySummary> GetActivitySummaryAsync(int id);
        Task<Employee?> GetByNIPAsync(string nip);
        /// <summary>Banyak karyawan sekaligus berdasarkan NIP (AKP & mitra; dipakai import pemkes).</summary>
        Task<List<Employee>> GetByNIPsAsync(IReadOnlyCollection<string> nips);
        Task<IEnumerable<Employee>> GetAllAsync(string? search = null, EmployeeScope scope = EmployeeScope.Akp);

        // Karyawan mitra kerja
        Task<List<Employee>> GetMitraEmployeesAsync(int? mitraId = null);
        Task<int> CreateMitraEmployeeAsync(Employee employee);
        Task UpdateMitraEmployeeAsync(Employee employee);
        Task<bool> DeleteMitraEmployeeAsync(int id);   // false kalau sudah punya riwayat medis
        Task<EmployeePageResult> GetPageAsync(EmployeePageQuery query);
        Task<List<Employee>> GetForExportAsync(EmployeePageQuery query);
        Task<int> CreateAsync(Employee employee);
        Task UpdateAsync(Employee employee);
        Task SetActiveAsync(int id, bool isActive);

        // Password Riwayat Saya (hash NULL = masih password awal = tanggal lahir)
        Task<string?> GetPortalPasswordHashAsync(int id);
        Task SetPortalPasswordAsync(int id, string passwordHash);
        Task ResetPortalPasswordAsync(int id);
        Task<int> CountAsync();
        Task DeleteAllAsync();
    }

    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public EmployeeRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Dipakai di semua query SELECT supaya nama Division/Department/Section/Position
        // ikut kebawa tanpa perlu query terpisah per karyawan. Pakai LEFT JOIN karena
        // keempatnya boleh kosong (NULL) - karyawan tetap valid walau belum diisi lengkap.
        private const string SelectWithJoinsSql = @"
            SELECT e.*,
                   dv.Name AS DivisionName,
                   dp.Name AS DepartmentName,
                   sc.Name AS SectionName,
                   ps.Name AS PositionName,
                   mt.Kode AS MitraKode,
                   mt.Nama AS MitraNama
            FROM dbo.Employees e
            LEFT JOIN dbo.Divisions dv ON dv.Id = e.DivisionId
            LEFT JOIN dbo.Departments dp ON dp.Id = e.DepartmentId
            LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
            LEFT JOIN dbo.Positions ps ON ps.Id = e.PositionId
            LEFT JOIN dbo.Mitras mt ON mt.Id = e.MitraId";

        public async Task<Employee?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectWithJoinsSql + " WHERE e.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<Employee>(sql, new { Id = id });
        }

        public async Task<EmployeeActivitySummary> GetActivitySummaryAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT
                    (SELECT COUNT(1) FROM dbo.FitToWorkSubmissions WHERE EmployeeId = @Id) AS FtwTotal,
                    (SELECT COUNT(1) FROM dbo.FitToWorkSubmissions WHERE EmployeeId = @Id AND KesiapanKerja LIKE 'YA%') AS FtwFit,
                    (SELECT MAX(CreatedAt) FROM dbo.FitToWorkSubmissions WHERE EmployeeId = @Id) AS FtwLastAt,
                    (SELECT COUNT(1) FROM dbo.GymAttendances WHERE EmployeeId = @Id) AS GymTotal,
                    (SELECT MAX(CreatedAt) FROM dbo.GymAttendances WHERE EmployeeId = @Id) AS GymLastAt,
                    (SELECT COUNT(1) FROM dbo.HealthCheckups WHERE EmployeeId = @Id) AS CheckupTotal,
                    (SELECT MAX(ExaminedAt) FROM dbo.HealthCheckups WHERE EmployeeId = @Id) AS CheckupLastAt,
                    (SELECT COUNT(1) FROM dbo.ClinicVisits WHERE EmployeeId = @Id AND IsCancelled = 0) AS VisitTotal,
                    (SELECT MAX(VisitAt) FROM dbo.ClinicVisits WHERE EmployeeId = @Id AND IsCancelled = 0) AS VisitLastAt";
            return await conn.QuerySingleAsync<EmployeeActivitySummary>(sql, new { Id = id });
        }

        public async Task<List<Employee>> GetByNIPsAsync(IReadOnlyCollection<string> nips)
        {
            var result = new List<Employee>();
            if (nips.Count == 0)
                return result;
            using var conn = _connectionFactory.CreateConnection();
            // Dipecah per 1000 NIP (batas parameter SQL Server 2100)
            foreach (var chunk in nips.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
                result.AddRange(await conn.QueryAsync<Employee>(SelectWithJoinsSql + " WHERE e.NIP IN @Nips", new { Nips = chunk }));
            return result;
        }

        public async Task<Employee?> GetByNIPAsync(string nip)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectWithJoinsSql + " WHERE e.NIP = @NIP";
            return await conn.QueryFirstOrDefaultAsync<Employee>(sql, new { NIP = nip });
        }

        public async Task<IEnumerable<Employee>> GetAllAsync(string? search = null, EmployeeScope scope = EmployeeScope.Akp)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectWithJoinsSql + @"
                WHERE (@Search IS NULL
                    OR e.NIP LIKE '%' + @Search + '%'
                    OR e.Nama LIKE '%' + @Search + '%'
                    OR sc.Name LIKE '%' + @Search + '%'
                    OR dp.Name LIKE '%' + @Search + '%'
                    OR dv.Name LIKE '%' + @Search + '%'
                    OR mt.Nama LIKE '%' + @Search + '%'
                    OR e.MitraNip LIKE '%' + @Search + '%')
                  AND (@Scope = 2 OR (@Scope = 0 AND e.MitraId IS NULL) OR (@Scope = 1 AND e.MitraId IS NOT NULL))
                ORDER BY e.Nama";
            return await conn.QueryAsync<Employee>(sql, new { Search = search, Scope = (int)scope });
        }

        public async Task<List<Employee>> GetMitraEmployeesAsync(int? mitraId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            // JumlahKunjungan dihitung terpisah lalu digabung (SelectWithJoinsSql dipakai bersama query lain)
            var sql = SelectWithJoinsSql + @"
                WHERE e.MitraId IS NOT NULL AND (@MitraId IS NULL OR e.MitraId = @MitraId)
                ORDER BY mt.Kode, e.Nama;

                SELECT v.EmployeeId, COUNT(1) AS Jumlah
                FROM dbo.ClinicVisits v
                INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId
                WHERE e.MitraId IS NOT NULL AND (@MitraId IS NULL OR e.MitraId = @MitraId)
                GROUP BY v.EmployeeId;

                SELECT t.EmployeeId, COUNT(1) AS Jumlah
                FROM dbo.DrugAlcoholTests t
                INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId
                WHERE e.MitraId IS NOT NULL AND (@MitraId IS NULL OR e.MitraId = @MitraId)
                GROUP BY t.EmployeeId;";
            using var multi = await conn.QueryMultipleAsync(sql, new { MitraId = mitraId });
            var list = (await multi.ReadAsync<Employee>()).ToList();
            var visits = (await multi.ReadAsync<VisitCountRow>()).ToDictionary(r => r.EmployeeId, r => r.Jumlah);
            var tests = (await multi.ReadAsync<VisitCountRow>()).ToDictionary(r => r.EmployeeId, r => r.Jumlah);
            foreach (var emp in list)
            {
                emp.JumlahKunjungan = visits.GetValueOrDefault(emp.Id);
                emp.JumlahTes = tests.GetValueOrDefault(emp.Id);
            }
            return list;
        }

        private sealed class VisitCountRow
        {
            public int EmployeeId { get; set; }
            public int Jumlah { get; set; }
        }

        public async Task<bool> DeleteMitraEmployeeAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            try
            {
                // Hanya bisa dihapus kalau belum punya data medis apa pun (riwayat kunjungan dll.)
                var affected = await conn.ExecuteAsync(@"
                    DELETE FROM dbo.Employees
                    WHERE Id = @Id AND MitraId IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM dbo.ClinicVisits WHERE EmployeeId = @Id)
                      AND NOT EXISTS (SELECT 1 FROM dbo.HealthCheckups WHERE EmployeeId = @Id)
                      AND NOT EXISTS (SELECT 1 FROM dbo.DrugAlcoholTests WHERE EmployeeId = @Id)", new { Id = id });
                return affected > 0;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
            {
                return false;   // masih direferensikan tabel lain (foreign key)
            }
        }

        public async Task<int> CreateMitraEmployeeAsync(Employee e)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.Employees (NIP, Nama, NomorHp, TanggalLahir, JenisKelamin, IsActive, MitraId, MitraNip, MitraJabatan, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@NIP, @Nama, @NomorHp, @TanggalLahir, @JenisKelamin, @IsActive, @MitraId, @MitraNip, @MitraJabatan, SYSUTCDATETIME())", e);
        }

        public async Task UpdateMitraEmployeeAsync(Employee e)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.Employees
                SET NIP = @NIP, Nama = @Nama, NomorHp = @NomorHp, TanggalLahir = @TanggalLahir, JenisKelamin = @JenisKelamin,
                    IsActive = @IsActive, MitraId = @MitraId, MitraNip = @MitraNip, MitraJabatan = @MitraJabatan
                WHERE Id = @Id AND MitraId IS NOT NULL", e);
        }

        // Urutan kolom HARUS sama dengan urutan <th> di Views/Employees/Index.cshtml.
        // Nama kolom SQL diambil dari daftar tetap ini (whitelist), bukan dari input browser,
        // karena ORDER BY tidak bisa pakai parameter @ - supaya aman dari SQL injection.
        private static readonly string[] SortColumns =
        {
            "e.NIP",        // 0
            "e.Nama",       // 1
            "dv.Name",      // 2 Division
            "dp.Name",      // 3 Department
            "sc.Name",      // 4 Section
            "ps.Name",      // 5 Position
            "e.NomorHp",    // 6
            "e.IsActive"    // 7 Status
        };

        private const string FromWithJoinsSql = @"
            FROM dbo.Employees e
            LEFT JOIN dbo.Divisions dv ON dv.Id = e.DivisionId
            LEFT JOIN dbo.Departments dp ON dp.Id = e.DepartmentId
            LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
            LEFT JOIN dbo.Positions ps ON ps.Id = e.PositionId";

        private const string PageWhereSql = @"
            WHERE e.MitraId IS NULL   -- Data Karyawan = karyawan AKP saja (mitra dikelola di menu Karyawan Mitra)
              AND (@Like IS NULL
                   OR e.NIP LIKE @Like OR e.Nama LIKE @Like OR e.NomorHp LIKE @Like
                   OR dv.Name LIKE @Like OR dp.Name LIKE @Like OR sc.Name LIKE @Like OR ps.Name LIKE @Like)
              AND (@IsActive IS NULL OR e.IsActive = @IsActive)";

        public async Task<EmployeePageResult> GetPageAsync(EmployeePageQuery query)
        {
            var sortColumn = SortColumns[Math.Clamp(query.SortColumn, 0, SortColumns.Length - 1)];
            var sortDir = query.SortDesc ? "DESC" : "ASC";

            // Karakter % _ [ punya arti khusus di LIKE, jadi di-escape supaya dicari sebagai teks biasa
            string? like = null;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var escaped = query.Search.Trim()
                    .Replace("[", "[[]")
                    .Replace("%", "[%]")
                    .Replace("_", "[_]");
                like = "%" + escaped + "%";
            }

            // 3 query dalam 1 kali kirim ke SQL Server:
            //   1) total semua karyawan, 2) total setelah filter, 3) data 1 halaman saja (OFFSET/FETCH)
            // e.Id ditambahkan di ORDER BY supaya urutan stabil (tidak ada baris dobel/hilang antar halaman).
            var sql = $@"
                SELECT COUNT(1) FROM dbo.Employees WHERE MitraId IS NULL;

                SELECT COUNT(1) {FromWithJoinsSql} {PageWhereSql};

                SELECT e.*,
                       dv.Name AS DivisionName,
                       dp.Name AS DepartmentName,
                       sc.Name AS SectionName,
                       ps.Name AS PositionName
                {FromWithJoinsSql}
                {PageWhereSql}
                ORDER BY {sortColumn} {sortDir}, e.Id {sortDir}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, new
            {
                Like = like,
                query.IsActive,
                Skip = Math.Max(query.Skip, 0),
                Take = Math.Clamp(query.Take, 1, 100)
            });

            return new EmployeePageResult
            {
                Total = await multi.ReadSingleAsync<int>(),
                Filtered = await multi.ReadSingleAsync<int>(),
                Rows = (await multi.ReadAsync<Employee>()).ToList()
            };
        }

        // Semua baris hasil filter (tanpa paging) untuk Download Excel - filter & urutan sama dengan tabel
        public async Task<List<Employee>> GetForExportAsync(EmployeePageQuery query)
        {
            var sortColumn = SortColumns[Math.Clamp(query.SortColumn, 0, SortColumns.Length - 1)];
            var sortDir = query.SortDesc ? "DESC" : "ASC";
            string? like = null;
            if (!string.IsNullOrWhiteSpace(query.Search))
                like = "%" + query.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

            var sql = $@"
                SELECT e.*,
                       dv.Name AS DivisionName,
                       dp.Name AS DepartmentName,
                       sc.Name AS SectionName,
                       ps.Name AS PositionName
                {FromWithJoinsSql}
                {PageWhereSql}
                ORDER BY {sortColumn} {sortDir}, e.Id {sortDir}";

            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<Employee>(sql, new { Like = like, query.IsActive })).ToList();
        }

        public async Task<int> CreateAsync(Employee employee)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.Employees (NIP, Nama, NomorHp, DivisionId, DepartmentId, SectionId, PositionId, TanggalLahir, JenisKelamin, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@NIP, @Nama, @NomorHp, @DivisionId, @DepartmentId, @SectionId, @PositionId, @TanggalLahir, @JenisKelamin, @IsActive, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, employee);
        }

        public async Task UpdateAsync(Employee employee)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                UPDATE dbo.Employees
                SET NIP = @NIP, Nama = @Nama, NomorHp = @NomorHp,
                    DivisionId = @DivisionId, DepartmentId = @DepartmentId,
                    SectionId = @SectionId, PositionId = @PositionId,
                    TanggalLahir = @TanggalLahir, JenisKelamin = @JenisKelamin, IsActive = @IsActive
                WHERE Id = @Id";
            await conn.ExecuteAsync(sql, employee);
        }

        public async Task SetActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.Employees SET IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, IsActive = isActive });
        }

        public async Task<string?> GetPortalPasswordHashAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<string?>(
                "SELECT PortalPasswordHash FROM dbo.Employees WHERE Id = @Id", new { Id = id });
        }

        public async Task SetPortalPasswordAsync(int id, string passwordHash)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.Employees
                SET PortalPasswordHash = @Hash, PortalPasswordChangedAt = SYSUTCDATETIME()
                WHERE Id = @Id", new { Id = id, Hash = passwordHash });
        }

        // Kembali ke password awal (tanggal lahir); karyawan wajib ganti lagi saat login berikutnya
        public async Task ResetPortalPasswordAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.Employees
                SET PortalPasswordHash = NULL, PortalPasswordChangedAt = NULL
                WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CountAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT COUNT(1) FROM dbo.Employees WHERE MitraId IS NULL";   // karyawan AKP saja
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        // Menghapus SEMUA data karyawan sekaligus SEMUA riwayat pengisian Fit To Work
        // mereka (wajib, karena FitToWorkSubmissions punya foreign key ke Employees -
        // tidak bisa hapus employee yang masih punya riwayat tanpa hapus riwayatnya dulu).
        // Dibungkus dalam satu transaksi supaya atomic: kalau salah satu langkah gagal,
        // semuanya di-rollback (tidak ada data yang kehapus setengah-setengah).
        public async Task DeleteAllAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                // Rekam medis (pemeriksaan & kunjungan klinik) TIDAK boleh ikut terhapus massal
                var medis = await conn.ExecuteScalarAsync<int>(@"
                    SELECT (SELECT COUNT(1) FROM dbo.HealthCheckups h INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId WHERE e.MitraId IS NULL)
                         + (SELECT COUNT(1) FROM dbo.ClinicVisits v INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId WHERE e.MitraId IS NULL)
                         + (SELECT COUNT(1) FROM dbo.DrugAlcoholTests t INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId WHERE e.MitraId IS NULL)",
                    transaction: transaction);
                if (medis > 0)
                    throw new InvalidOperationException(
                        "Tidak bisa menghapus semua karyawan karena sudah ada rekam medis (pemeriksaan/kunjungan klinik/tes urin & alkohol). " +
                        "Nonaktifkan karyawan satu per satu sebagai gantinya.");

                // Karyawan mitra (menu Karyawan Mitra) tidak ikut terhapus
                await conn.ExecuteAsync("DELETE FROM dbo.FitToWorkSubmissions", transaction: transaction);
                await conn.ExecuteAsync("DELETE FROM dbo.GymAttendances", transaction: transaction);
                await conn.ExecuteAsync(@"UPDATE dbo.AdminUsers SET EmployeeId = NULL
                    WHERE EmployeeId IN (SELECT Id FROM dbo.Employees WHERE MitraId IS NULL)", transaction: transaction);
                await conn.ExecuteAsync("DELETE FROM dbo.Employees WHERE MitraId IS NULL", transaction: transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}