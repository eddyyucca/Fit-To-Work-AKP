using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;
using FitToWorkApp.Services;

namespace FitToWorkApp.Repositories
{
    /// <summary>Parameter untuk ambil riwayat aktivitas per halaman (server-side DataTables).</summary>
    public class GymPageQuery
    {
        public string? Search { get; set; }          // cari di NIP / nama / department / jabatan / aktivitas
        public DateTime? FromUtc { get; set; }       // batas bawah CreatedAt / mulai (UTC, inklusif)
        public DateTime? ToUtc { get; set; }         // batas atas CreatedAt / mulai (UTC, eksklusif)
        public string? Department { get; set; }
        public string? Position { get; set; }
        public int? ActivityTypeId { get; set; }
        /// <summary>'ingym' | 'selesai' | 'tidak' | 'lama' | null (semua)</summary>
        public string? Status { get; set; }
        public int SortColumn { get; set; }          // index kolom tabel (lihat GymSortColumns)
        public bool SortDesc { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public class GymPageResult
    {
        public int Total { get; set; }               // total semua data (tanpa filter)
        public int Filtered { get; set; }            // total setelah filter
        public int FilteredEmployees { get; set; }   // jumlah karyawan berbeda dari hasil filter
        public decimal? RataDurasiMenit { get; set; } // rata-rata durasi sesi yang diselesaikan sendiri (hasil filter)
        public int TidakCheckout { get; set; }       // sesi tidak diselesaikan (hasil filter)
        public long TotalJarakMeter { get; set; }    // total jarak tempuh GPS (hasil filter)
        public int SedangDiGym { get; set; }         // sesi berlangsung saat ini (semua karyawan, tanpa filter)
        public List<GymAttendance> Rows { get; set; } = new();
    }

    /// <summary>Hasil GPS yang disimpan saat aktivitas diselesaikan.</summary>
    public record GymTrackSave(int? JarakTempuhMeter, string? RutePolyline, int? GpsTitik, int? GpsCelahDetik);

    /// <summary>
    /// Sesi aktivitas kebugaran (tabel dbo.GymAttendances; nama lama karena fitur berawal dari gym).
    /// Sesi berlangsung = CheckOutStatus NULL dan belum lewat BatasAt.
    /// </summary>
    public interface IGymRepository
    {
        /// <summary>
        /// Mulai aktivitas: tutup dulu sesi basi milik karyawan ini, lalu simpan sesi baru dengan BatasAt = sekarang + maksJam.
        /// Melempar SqlException 2601/2627 kalau karyawan masih punya sesi berlangsung (index UX_GymAttendances_Open).
        /// </summary>
        Task<int> CreateAsync(GymAttendance attendance, int maksJam);
        Task<GymAttendance?> GetByIdAsync(int id);

        /// <summary>Sesi yang masih berlangsung (belum selesai & belum lewat batas), atau null.</summary>
        Task<GymAttendance?> GetOpenAsync(int employeeId);

        /// <summary>Selesaikan sesi milik karyawan. false kalau sesi sudah tertutup / lewat batas.</summary>
        Task<bool> CheckOutAsync(int id, int employeeId, int? jarakMeter, GymTrackSave? track);

        /// <summary>Tandai sesi yang lewat batas sebagai 'Auto' (tidak diselesaikan). employeeId null = semua.</summary>
        Task<int> CloseStaleAsync(int? employeeId = null);

        Task<List<GymAttendance>> GetByEmployeeAsync(int employeeId, int top);
        Task<GymPageResult> GetPageAsync(GymPageQuery query);
        Task<List<GymAttendance>> GetForExportAsync(GymPageQuery query, int maxRows);
    }

    public class GymRepository : IGymRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public GymRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>Batas sesi; data lama tanpa BatasAt = check-in + 3 jam.</summary>
        public const string DeadlineSql = "ISNULL(g.BatasAt, DATEADD(HOUR, 3, g.CreatedAt))";

        // Kolom untuk daftar (tanpa RutePolyline yang bisa besar)
        private const string ListColumns = @"
            g.Id, g.EmployeeId, g.NamaSnapshot, g.DepartmentSnapshot, g.SectionSnapshot, g.PositionSnapshot, g.BeratBadan,
            g.CreatedAt, g.CheckOutAt, g.CheckOutStatus, g.CheckOutJarakMeter,
            g.LokasiSiteId, g.LokasiNama, g.GpsLat, g.GpsLng, g.GpsAkurasi, g.JarakMeter, g.LokasiVia,
            g.ActivityTypeId, g.AktivitasNama, g.AktivitasIkon, g.PakaiGps, g.KunciLokasi, g.BatasAt,
            g.JarakTempuhMeter, g.GpsTitik, g.GpsCelahDetik,
            CAST(CASE WHEN g.RutePolyline IS NULL THEN 0 ELSE 1 END AS BIT) AS PunyaRute,
            e.NIP";

        private const string CloseStaleSql = @"
            UPDATE g
            SET CheckOutStatus = 'Auto'
            FROM dbo.GymAttendances g
            WHERE g.CheckOutStatus IS NULL
              AND " + DeadlineSql + @" <= SYSUTCDATETIME()
              AND (@EmployeeId IS NULL OR g.EmployeeId = @EmployeeId)";

        public async Task<int> CreateAsync(GymAttendance attendance, int maksJam)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            await conn.ExecuteAsync(CloseStaleSql, new { EmployeeId = (int?)attendance.EmployeeId }, tx);

            const string sql = @"
                DECLARE @Now DATETIME2 = SYSUTCDATETIME();
                INSERT INTO dbo.GymAttendances
                    (EmployeeId, NamaSnapshot, DepartmentSnapshot, SectionSnapshot, PositionSnapshot, BeratBadan, CreatedAt,
                     ActivityTypeId, AktivitasNama, AktivitasIkon, PakaiGps, KunciLokasi, BatasAt,
                     LokasiSiteId, LokasiNama, GpsLat, GpsLng, GpsAkurasi, JarakMeter, LokasiVia)
                OUTPUT INSERTED.Id
                VALUES
                    (@EmployeeId, @NamaSnapshot, @DepartmentSnapshot, @SectionSnapshot, @PositionSnapshot, @BeratBadan, @Now,
                     @ActivityTypeId, @AktivitasNama, @AktivitasIkon, @PakaiGps, @KunciLokasi, DATEADD(HOUR, @MaksJam, @Now),
                     @LokasiSiteId, @LokasiNama, @GpsLat, @GpsLng, @GpsAkurasi, @JarakMeter, @LokasiVia)";

            var p = new DynamicParameters(attendance);
            p.Add("MaksJam", Math.Clamp(maksJam, ActivityType.MinJam, ActivityType.MaxJam));
            var id = await conn.ExecuteScalarAsync<int>(sql, p, tx);

            tx.Commit();
            return id;
        }

        public async Task<GymAttendance?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT g.*, e.NIP, CAST(CASE WHEN g.RutePolyline IS NULL THEN 0 ELSE 1 END AS BIT) AS PunyaRute
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                WHERE g.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<GymAttendance>(sql, new { Id = id });
        }

        public async Task<GymAttendance?> GetOpenAsync(int employeeId)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = $@"
                SELECT TOP 1 {ListColumns}
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                WHERE g.EmployeeId = @EmployeeId
                  AND g.CheckOutStatus IS NULL
                  AND {DeadlineSql} > SYSUTCDATETIME()
                ORDER BY g.CreatedAt DESC";
            return await conn.QueryFirstOrDefaultAsync<GymAttendance>(sql, new { EmployeeId = employeeId });
        }

        public async Task<bool> CheckOutAsync(int id, int employeeId, int? jarakMeter, GymTrackSave? track)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                UPDATE g
                SET CheckOutAt = SYSUTCDATETIME(), CheckOutStatus = 'Manual', CheckOutJarakMeter = @JarakMeter,
                    JarakTempuhMeter = @JarakTempuhMeter, RutePolyline = @RutePolyline,
                    GpsTitik = @GpsTitik, GpsCelahDetik = @GpsCelahDetik
                FROM dbo.GymAttendances g
                WHERE g.Id = @Id AND g.EmployeeId = @EmployeeId
                  AND g.CheckOutStatus IS NULL
                  AND " + DeadlineSql + " > SYSUTCDATETIME()";
            var rows = await conn.ExecuteAsync(sql, new
            {
                Id = id,
                EmployeeId = employeeId,
                JarakMeter = jarakMeter,
                track?.JarakTempuhMeter,
                track?.RutePolyline,
                track?.GpsTitik,
                track?.GpsCelahDetik
            });
            return rows == 1;
        }

        public async Task<int> CloseStaleAsync(int? employeeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteAsync(CloseStaleSql, new { EmployeeId = employeeId });
        }

        public async Task<List<GymAttendance>> GetByEmployeeAsync(int employeeId, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = $@"
                SELECT TOP (@Top) {ListColumns}
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                WHERE g.EmployeeId = @EmployeeId
                ORDER BY g.CreatedAt DESC";
            return (await conn.QueryAsync<GymAttendance>(sql, new { EmployeeId = employeeId, Top = top })).ToList();
        }

        // Durasi hanya untuk sesi yang diselesaikan sendiri
        private const string DurasiSql = "CASE WHEN g.CheckOutStatus = 'Manual' THEN DATEDIFF(MINUTE, g.CreatedAt, g.CheckOutAt) END";
        private const string JarakSql = "CASE WHEN g.CheckOutStatus = 'Manual' THEN g.JarakTempuhMeter END";

        // Urutan HARUS sama dengan urutan <th> di Views/GymHistory/Index.cshtml (whitelist, aman dari SQL injection)
        private static readonly string[] GymSortColumns =
        {
            "g.CreatedAt",           // 0 Mulai
            "e.NIP",                 // 1
            "g.NamaSnapshot",        // 2 Nama
            "g.DepartmentSnapshot",  // 3 Department
            "g.AktivitasNama",       // 4 Aktivitas
            "g.CheckOutAt",          // 5 Selesai
            DurasiSql,               // 6 Durasi
            JarakSql,                // 7 Jarak
            "g.CheckOutStatus",      // 8 Status
            "g.BeratBadan",          // 9 Berat
            "g.LokasiNama"           // 10 Lokasi
        };

        private const string GymWhereSql = @"
            WHERE (@FromUtc IS NULL OR g.CreatedAt >= @FromUtc)
              AND (@ToUtc   IS NULL OR g.CreatedAt <  @ToUtc)
              AND (@Department IS NULL OR g.DepartmentSnapshot = @Department)
              AND (@Position IS NULL OR g.PositionSnapshot = @Position)
              AND (@ActivityTypeId IS NULL OR g.ActivityTypeId = @ActivityTypeId)
              AND (@Status IS NULL
                   OR (@Status = 'ingym'   AND g.CheckOutStatus IS NULL AND " + DeadlineSql + @" > @NowUtc)
                   OR (@Status = 'selesai' AND g.CheckOutStatus = 'Manual')
                   OR (@Status = 'tidak'   AND (g.CheckOutStatus = 'Auto' OR (g.CheckOutStatus IS NULL AND " + DeadlineSql + @" <= @NowUtc)))
                   OR (@Status = 'lama'    AND g.CheckOutStatus = 'Legacy'))
              AND (@Like IS NULL
                   OR e.NIP LIKE @Like OR g.NamaSnapshot LIKE @Like OR g.AktivitasNama LIKE @Like
                   OR g.DepartmentSnapshot LIKE @Like OR g.PositionSnapshot LIKE @Like)";

        private static readonly HashSet<string> ValidStatus = new(StringComparer.OrdinalIgnoreCase) { "ingym", "selesai", "tidak", "lama" };

        private class GymCounts
        {
            public int Filtered { get; set; }
            public int FilteredEmployees { get; set; }
            public decimal? RataDurasiMenit { get; set; }
            public int TidakCheckout { get; set; }
            public long TotalJarakMeter { get; set; }
        }

        private static object BuildParams(GymPageQuery query) => new
        {
            query.FromUtc,
            query.ToUtc,
            Department = string.IsNullOrWhiteSpace(query.Department) ? null : query.Department,
            Position = string.IsNullOrWhiteSpace(query.Position) ? null : query.Position,
            ActivityTypeId = query.ActivityTypeId is > 0 ? query.ActivityTypeId : null,
            Status = query.Status is not null && ValidStatus.Contains(query.Status) ? query.Status.ToLowerInvariant() : null,
            NowUtc = DateTime.UtcNow,
            Like = BuildLike(query.Search),
            Skip = Math.Max(query.Skip, 0),
            Take = Math.Clamp(query.Take, 1, 100)
        };

        private static string? BuildLike(string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return null;
            var escaped = search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            return "%" + escaped + "%";
        }

        private static string OrderBySql(GymPageQuery query)
        {
            var sortColumn = GymSortColumns[Math.Clamp(query.SortColumn, 0, GymSortColumns.Length - 1)];
            var sortDir = query.SortDesc ? "DESC" : "ASC";
            return $"ORDER BY {sortColumn} {sortDir}, g.Id {sortDir}";
        }

        public async Task<GymPageResult> GetPageAsync(GymPageQuery query)
        {
            var sql = $@"
                SELECT COUNT(1) FROM dbo.GymAttendances;

                SELECT COUNT(1) FROM dbo.GymAttendances g
                WHERE g.CheckOutStatus IS NULL AND {DeadlineSql} > @NowUtc;

                SELECT COUNT(1) AS Filtered,
                       COUNT(DISTINCT g.EmployeeId) AS FilteredEmployees,
                       AVG(CAST({DurasiSql} AS DECIMAL(10,1))) AS RataDurasiMenit,
                       ISNULL(SUM(CASE WHEN g.CheckOutStatus = 'Auto' OR (g.CheckOutStatus IS NULL AND {DeadlineSql} <= @NowUtc)
                                       THEN 1 ELSE 0 END), 0) AS TidakCheckout,
                       ISNULL(SUM(CAST({JarakSql} AS BIGINT)), 0) AS TotalJarakMeter
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                {GymWhereSql};

                SELECT {ListColumns}
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                {GymWhereSql}
                {OrderBySql(query)}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, BuildParams(query));

            var total = await multi.ReadSingleAsync<int>();
            var sedangDiGym = await multi.ReadSingleAsync<int>();
            var counts = await multi.ReadSingleAsync<GymCounts>();
            var rows = (await multi.ReadAsync<GymAttendance>()).ToList();

            return new GymPageResult
            {
                Total = total,
                Filtered = counts.Filtered,
                FilteredEmployees = counts.FilteredEmployees,
                RataDurasiMenit = counts.RataDurasiMenit,
                TidakCheckout = counts.TidakCheckout,
                TotalJarakMeter = counts.TotalJarakMeter,
                SedangDiGym = sedangDiGym,
                Rows = rows
            };
        }

        public async Task<List<GymAttendance>> GetForExportAsync(GymPageQuery query, int maxRows)
        {
            var sql = $@"
                SELECT TOP (@MaxRows) {ListColumns}
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                {GymWhereSql}
                {OrderBySql(query)}";

            var p = new DynamicParameters(BuildParams(query));
            p.Add("MaxRows", maxRows);

            using var conn = _connectionFactory.CreateConnection();
            var rows = await conn.QueryAsync<GymAttendance>(sql, p);
            return rows.ToList();
        }
    }
}
