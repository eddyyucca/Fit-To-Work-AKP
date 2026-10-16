using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public class MedicalPageQuery
    {
        public string? Search { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string? Department { get; set; }
        public string? Status { get; set; }          // kunjungan: rekomendasi / "batal"
        public int SortColumn { get; set; }
        public bool SortDesc { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public class MedicalPageResult<T>
    {
        public int Total { get; set; }
        public int Filtered { get; set; }
        public int FilteredEmployees { get; set; }
        public List<T> Rows { get; set; } = new();
    }

    internal class MedicalCounts
    {
        public int Filtered { get; set; }
        public int FilteredEmployees { get; set; }
    }

    internal static class MedicalSql
    {
        public static string? Like(string? search) => string.IsNullOrWhiteSpace(search)
            ? null
            : "%" + search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
    }

    public interface IHealthCheckupRepository
    {
        Task<int> CreateAsync(HealthCheckup c);

        /// <summary>Waktu pemeriksaan (UTC) karyawan-karyawan ini dalam rentang waktu (untuk cek dobel saat import).</summary>
        Task<List<EmployeeExamTime>> GetExamTimesAsync(IReadOnlyCollection<int> employeeIds, DateTime fromUtc, DateTime toUtc);

        /// <summary>
        /// Simpan banyak pemeriksaan sekaligus (import Excel) dalam 1 transaksi. Pemeriksaan yang karyawannya sudah punya
        /// pemkes di hari yang sama (dicek ulang di dalam transaksi) dilewati. Mengembalikan nomor baris yang dilewati.
        /// </summary>
        Task<List<int>> CreateManyAsync(IReadOnlyList<CheckupImportItem> items);
        Task<HealthCheckup?> GetByIdAsync(int id);
        Task DeleteAsync(int id);
        Task<MedicalPageResult<HealthCheckup>> GetPageAsync(MedicalPageQuery q);
        Task<List<HealthCheckup>> GetForExportAsync(MedicalPageQuery q, int maxRows);
        Task<List<HealthCheckup>> GetByEmployeeAsync(int employeeId, int top = 200);

        // ---------- Temuan & follow up ----------
        Task UpdateFollowUpAsync(int id, bool adaTemuan, string? keterangan, DateTime? followUpDate);
        Task CompleteFollowUpAsync(int id, string hasil, string doneBy);
        Task ReopenFollowUpAsync(int id);

        /// <summary>Daftar follow up. status: open (default: belum selesai), overdue, today, done, all.</summary>
        Task<List<HealthCheckup>> GetFollowUpsAsync(string? status, DateTime today, int top = 2000);
        Task<FollowUpCounts> CountFollowUpsAsync(DateTime today);
    }

    public class EmployeeExamTime
    {
        public int EmployeeId { get; set; }
        public DateTime ExaminedAt { get; set; }   // UTC
    }

    /// <summary>Satu pemeriksaan hasil import + rentang "hari yang sama" (UTC) untuk cek dobel.</summary>
    public class CheckupImportItem
    {
        public int RowNumber { get; set; }
        public HealthCheckup Checkup { get; set; } = new();
        public DateTime DayStartUtc { get; set; }
        public DateTime DayEndUtc { get; set; }
    }

    public class FollowUpCounts
    {
        public int Open { get; set; }
        public int Overdue { get; set; }
        public int Today { get; set; }
    }

    public class HealthCheckupRepository : IHealthCheckupRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public HealthCheckupRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> CreateAsync(HealthCheckup c)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var id = await InsertAsync(conn, tx, c);
                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<List<EmployeeExamTime>> GetExamTimesAsync(IReadOnlyCollection<int> employeeIds, DateTime fromUtc, DateTime toUtc)
        {
            var result = new List<EmployeeExamTime>();
            if (employeeIds.Count == 0)
                return result;
            using var conn = _connectionFactory.CreateConnection();
            // Dipecah per 1000 id (batas parameter SQL Server 2100)
            foreach (var chunk in employeeIds.Distinct().Chunk(1000))
            {
                result.AddRange(await conn.QueryAsync<EmployeeExamTime>(@"
                    SELECT EmployeeId, ExaminedAt FROM dbo.HealthCheckups
                    WHERE EmployeeId IN @Ids AND ExaminedAt >= @FromUtc AND ExaminedAt < @ToUtc",
                    new { Ids = chunk, FromUtc = fromUtc, ToUtc = toUtc }));
            }
            return result;
        }

        public async Task<List<int>> CreateManyAsync(IReadOnlyList<CheckupImportItem> items)
        {
            var skipped = new List<int>();
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var item in items)
                {
                    // Cek ulang di dalam transaksi (UPDLOCK): mencegah dobel kalau 2 admin import file yang sama bersamaan
                    var exists = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(1) FROM dbo.HealthCheckups WITH (UPDLOCK, HOLDLOCK)
                        WHERE EmployeeId = @EmployeeId AND ExaminedAt >= @FromUtc AND ExaminedAt < @ToUtc",
                        new { item.Checkup.EmployeeId, FromUtc = item.DayStartUtc, ToUtc = item.DayEndUtc }, tx);
                    if (exists > 0)
                    {
                        skipped.Add(item.RowNumber);
                        continue;
                    }
                    await InsertAsync(conn, tx, item.Checkup);
                }
                tx.Commit();
                return skipped;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static async Task<int> InsertAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, HealthCheckup c)
        {
            var id = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.HealthCheckups
                    (EmployeeId, NamaSnapshot, DepartmentSnapshot, SectionSnapshot, PositionSnapshot, JenisKelamin,
                     ExaminedAt, Sistolik, Diastolik, BeratBadan, TinggiBadan, LingkarPerut, Kolesterol, AsamUrat,
                     GulaDarah, GulaDarahJenis, Keluhan, Catatan, AdaTemuan, TemuanKeterangan, FollowUpDate,
                     UsesParameters, TotalSkor, SkorParamTerisi, SkorParamTotal, RisikoLabel, RisikoColor,
                     ExaminerAdminId, ExaminerName, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES
                    (@EmployeeId, @NamaSnapshot, @DepartmentSnapshot, @SectionSnapshot, @PositionSnapshot, @JenisKelamin,
                     @ExaminedAt, @Sistolik, @Diastolik, @BeratBadan, @TinggiBadan, @LingkarPerut, @Kolesterol, @AsamUrat,
                     @GulaDarah, @GulaDarahJenis, @Keluhan, @Catatan, @AdaTemuan, @TemuanKeterangan, @FollowUpDate,
                     @UsesParameters, @TotalSkor, @SkorParamTerisi, @SkorParamTotal, @RisikoLabel, @RisikoColor,
                     @ExaminerAdminId, @ExaminerName, SYSUTCDATETIME())", c, tx);

            foreach (var v in c.Values)
            {
                v.CheckupId = id;
                await conn.ExecuteAsync(@"
                    INSERT INTO dbo.HealthCheckupValues
                        (CheckupId, ParameterId, ParameterName, Unit, InputType, SortOrder, NumericValue, TextValue,
                         CriteriaLabel, Points, Level, IsScored)
                    VALUES
                        (@CheckupId, @ParameterId, @ParameterName, @Unit, @InputType, @SortOrder, @NumericValue, @TextValue,
                         @CriteriaLabel, @Points, @Level, @IsScored)", v, tx);
            }
            return id;
        }

        /// <summary>Isi daftar nilai parameter (format baru) untuk pemeriksaan-pemeriksaan ini.</summary>
        private static async Task LoadValuesAsync(System.Data.IDbConnection conn, IReadOnlyCollection<HealthCheckup> checkups)
        {
            var ids = checkups.Where(c => c.UsesParameters).Select(c => c.Id).ToList();
            if (ids.Count == 0)
                return;
            var values = new List<HealthCheckupValue>();
            // Dipecah per 1000 id karena SQL Server membatasi jumlah parameter (maks. 2100)
            foreach (var chunk in ids.Chunk(1000))
                values.AddRange(await conn.QueryAsync<HealthCheckupValue>(
                    "SELECT * FROM dbo.HealthCheckupValues WHERE CheckupId IN @Ids ORDER BY CheckupId, SortOrder, Id", new { Ids = chunk }));
            var byCheckup = values.GroupBy(v => v.CheckupId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var c in checkups)
                c.Values = byCheckup.TryGetValue(c.Id, out var list) ? list : new List<HealthCheckupValue>();
        }

        // Jumlah lampiran (upload hasil pemeriksaan) per pemeriksaan
        private const string AttachmentCountSql = @"
            (SELECT COUNT(1) FROM dbo.MedicalAttachments a WHERE a.EntityType = 'Checkup' AND a.EntityId = h.Id) AS AttachmentCount";

        public async Task<HealthCheckup?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            var c = await conn.QueryFirstOrDefaultAsync<HealthCheckup>($@"
                SELECT h.*, e.NIP, {AttachmentCountSql} FROM dbo.HealthCheckups h
                INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId
                WHERE h.Id = @Id", new { Id = id });
            if (c is not null)
                await LoadValuesAsync(conn, new[] { c });
            return c;
        }

        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.HealthCheckups WHERE Id = @Id", new { Id = id });
        }

        private static readonly string[] SortColumns =
        {
            // urutan = kolom tabel di Views/Checkups/Index.cshtml (0 Waktu, 1 NIP, 2 Nama, 3 Department, 4 Skor, 5 Risiko)
            "h.ExaminedAt", "e.NIP", "h.NamaSnapshot", "h.DepartmentSnapshot", "h.TotalSkor", "h.TotalSkor"
        };

        private const string WhereSql = @"
            WHERE (@FromUtc IS NULL OR h.ExaminedAt >= @FromUtc)
              AND (@ToUtc   IS NULL OR h.ExaminedAt <  @ToUtc)
              AND (@Department IS NULL OR h.DepartmentSnapshot = @Department)
              AND (@Like IS NULL OR e.NIP LIKE @Like OR h.NamaSnapshot LIKE @Like OR h.DepartmentSnapshot LIKE @Like
                   OR h.Keluhan LIKE @Like)";

        private static object Params(MedicalPageQuery q) => new
        {
            q.FromUtc, q.ToUtc,
            Department = string.IsNullOrWhiteSpace(q.Department) ? null : q.Department,
            Like = MedicalSql.Like(q.Search),
            Skip = Math.Max(q.Skip, 0),
            Take = Math.Clamp(q.Take, 1, 100)
        };

        private static string OrderBy(MedicalPageQuery q)
        {
            var dir = q.SortDesc ? "DESC" : "ASC";
            return $"ORDER BY {SortColumns[Math.Clamp(q.SortColumn, 0, SortColumns.Length - 1)]} {dir}, h.Id {dir}";
        }

        public async Task<MedicalPageResult<HealthCheckup>> GetPageAsync(MedicalPageQuery q)
        {
            var sql = $@"
                SELECT COUNT(1) FROM dbo.HealthCheckups;
                SELECT COUNT(1) AS Filtered, COUNT(DISTINCT h.EmployeeId) AS FilteredEmployees
                FROM dbo.HealthCheckups h INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId {WhereSql};
                SELECT h.*, e.NIP, {AttachmentCountSql} FROM dbo.HealthCheckups h INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId
                {WhereSql} {OrderBy(q)}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, Params(q));
            var total = await multi.ReadSingleAsync<int>();
            var counts = await multi.ReadSingleAsync<MedicalCounts>();
            var rows = (await multi.ReadAsync<HealthCheckup>()).ToList();
            await LoadValuesAsync(conn, rows);
            return new MedicalPageResult<HealthCheckup>
            {
                Total = total,
                Filtered = counts.Filtered,
                FilteredEmployees = counts.FilteredEmployees,
                Rows = rows
            };
        }

        public async Task<List<HealthCheckup>> GetForExportAsync(MedicalPageQuery q, int maxRows)
        {
            var sql = $@"
                SELECT TOP (@MaxRows) h.*, e.NIP FROM dbo.HealthCheckups h
                INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId
                {WhereSql} {OrderBy(q)}";
            var p = new DynamicParameters(Params(q));
            p.Add("MaxRows", maxRows);
            using var conn = _connectionFactory.CreateConnection();
            var rows = (await conn.QueryAsync<HealthCheckup>(sql, p)).ToList();
            await LoadValuesAsync(conn, rows);
            return rows;
        }

        public async Task<List<HealthCheckup>> GetByEmployeeAsync(int employeeId, int top = 200)
        {
            using var conn = _connectionFactory.CreateConnection();
            var rows = (await conn.QueryAsync<HealthCheckup>($@"
                SELECT TOP (@Top) h.*, e.NIP, {AttachmentCountSql} FROM dbo.HealthCheckups h
                INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId
                WHERE h.EmployeeId = @EmployeeId
                ORDER BY h.ExaminedAt DESC, h.Id DESC", new { EmployeeId = employeeId, Top = top })).ToList();
            await LoadValuesAsync(conn, rows);
            return rows;
        }

        // ======================= Temuan & follow up =======================

        public async Task UpdateFollowUpAsync(int id, bool adaTemuan, string? keterangan, DateTime? followUpDate)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.HealthCheckups
                SET AdaTemuan = @AdaTemuan,
                    TemuanKeterangan = @Keterangan,
                    FollowUpDate = @FollowUpDate,
                    -- temuan dihapus -> status selesai ikut dibersihkan
                    FollowUpDoneAt = CASE WHEN @AdaTemuan = 1 THEN FollowUpDoneAt END,
                    FollowUpDoneBy = CASE WHEN @AdaTemuan = 1 THEN FollowUpDoneBy END,
                    FollowUpHasil  = CASE WHEN @AdaTemuan = 1 THEN FollowUpHasil END
                WHERE Id = @Id",
                new { Id = id, AdaTemuan = adaTemuan, Keterangan = adaTemuan ? keterangan : null, FollowUpDate = adaTemuan ? followUpDate?.Date : null });
        }

        public async Task CompleteFollowUpAsync(int id, string hasil, string doneBy)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.HealthCheckups
                SET FollowUpDoneAt = SYSUTCDATETIME(), FollowUpDoneBy = @By, FollowUpHasil = @Hasil
                WHERE Id = @Id AND AdaTemuan = 1", new { Id = id, Hasil = hasil, By = doneBy });
        }

        public async Task ReopenFollowUpAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.HealthCheckups SET FollowUpDoneAt = NULL, FollowUpDoneBy = NULL
                WHERE Id = @Id AND AdaTemuan = 1", new { Id = id });
        }

        public async Task<List<HealthCheckup>> GetFollowUpsAsync(string? status, DateTime today, int top = 2000)
        {
            var where = status switch
            {
                "overdue" => "h.FollowUpDoneAt IS NULL AND h.FollowUpDate < @Today",
                "today" => "h.FollowUpDoneAt IS NULL AND h.FollowUpDate = @Today",
                "done" => "h.FollowUpDoneAt IS NOT NULL",
                "all" => "1 = 1",
                _ => "h.FollowUpDoneAt IS NULL"
            };
            // Belum selesai: yang paling mendesak dulu. Selesai: yang terbaru dulu.
            var order = status == "done" ? "h.FollowUpDoneAt DESC" : "CASE WHEN h.FollowUpDoneAt IS NULL THEN 0 ELSE 1 END, h.FollowUpDate, h.Id";

            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<HealthCheckup>($@"
                SELECT TOP (@Top) h.*, e.NIP, {AttachmentCountSql}
                FROM dbo.HealthCheckups h
                INNER JOIN dbo.Employees e ON e.Id = h.EmployeeId
                WHERE h.AdaTemuan = 1 AND {where}
                ORDER BY {order}", new { Today = today.Date, Top = top })).ToList();
        }

        public async Task<FollowUpCounts> CountFollowUpsAsync(DateTime today)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QuerySingleAsync<FollowUpCounts>(@"
                SELECT COUNT(1) AS [Open],
                       ISNULL(SUM(CASE WHEN FollowUpDate < @Today THEN 1 ELSE 0 END), 0) AS Overdue,
                       ISNULL(SUM(CASE WHEN FollowUpDate = @Today THEN 1 ELSE 0 END), 0) AS Today
                FROM dbo.HealthCheckups
                WHERE AdaTemuan = 1 AND FollowUpDoneAt IS NULL", new { Today = today.Date });
        }
    }
}
