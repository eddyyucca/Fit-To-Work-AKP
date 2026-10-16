using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public class AccessLogPageQuery
    {
        public string? Search { get; set; }          // cari di username / aktivitas / path / IP
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string? Username { get; set; }
        public string? EventType { get; set; }
        public int SortColumn { get; set; }
        public bool SortDesc { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public class AccessLogPageResult
    {
        public int Total { get; set; }
        public int Filtered { get; set; }
        public int FilteredFailed { get; set; }      // login gagal + akses ditolak dari hasil filter
        public List<AccessLog> Rows { get; set; } = new();
    }

    public interface IAccessLogRepository
    {
        Task InsertAsync(AccessLog log);
        Task<AccessLogPageResult> GetPageAsync(AccessLogPageQuery query);
        Task<List<AccessLog>> GetForExportAsync(AccessLogPageQuery query, int maxRows);
        Task<List<string>> GetUsernamesAsync();
        Task<int> DeleteOlderThanAsync(DateTime cutoffUtc);
    }

    public class AccessLogRepository : IAccessLogRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AccessLogRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InsertAsync(AccessLog log)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.AccessLogs
                    (CreatedAt, AdminUserId, Username, EventType, Activity, HttpMethod, Path, StatusCode, IpAddress, UserAgent)
                VALUES
                    (SYSUTCDATETIME(), @AdminUserId, @Username, @EventType, @Activity, @HttpMethod, @Path, @StatusCode, @IpAddress, @UserAgent)";
            await conn.ExecuteAsync(sql, log);
        }

        // Urutan HARUS sama dengan urutan <th> di Views/AccessLogs/Index.cshtml
        private static readonly string[] SortColumns =
        {
            "l.CreatedAt",   // 0 Waktu
            "l.Username",    // 1 User
            "l.EventType",   // 2 Jenis
            "l.Activity",    // 3 Aktivitas
            "l.IpAddress"    // 4 IP
        };

        private const string WhereSql = @"
            WHERE (@FromUtc IS NULL OR l.CreatedAt >= @FromUtc)
              AND (@ToUtc   IS NULL OR l.CreatedAt <  @ToUtc)
              AND (@Username IS NULL OR l.Username = @Username)
              AND (@EventType IS NULL OR l.EventType = @EventType)
              AND (@Like IS NULL
                   OR l.Username LIKE @Like OR l.Activity LIKE @Like OR l.Path LIKE @Like OR l.IpAddress LIKE @Like)";

        private static object BuildParams(AccessLogPageQuery q)
        {
            string? like = null;
            if (!string.IsNullOrWhiteSpace(q.Search))
                like = "%" + q.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

            return new
            {
                q.FromUtc,
                q.ToUtc,
                Username = string.IsNullOrWhiteSpace(q.Username) ? null : q.Username,
                EventType = string.IsNullOrWhiteSpace(q.EventType) ? null : q.EventType,
                Like = like,
                Skip = Math.Max(q.Skip, 0),
                Take = Math.Clamp(q.Take, 1, 100)
            };
        }

        private static string OrderBySql(AccessLogPageQuery q) =>
            $"ORDER BY {SortColumns[Math.Clamp(q.SortColumn, 0, SortColumns.Length - 1)]} {(q.SortDesc ? "DESC" : "ASC")}, l.Id {(q.SortDesc ? "DESC" : "ASC")}";

        private class Counts
        {
            public int Filtered { get; set; }
            public int FilteredFailed { get; set; }
        }

        public async Task<AccessLogPageResult> GetPageAsync(AccessLogPageQuery query)
        {
            var sql = $@"
                SELECT COUNT(1) FROM dbo.AccessLogs;

                SELECT COUNT(1) AS Filtered,
                       ISNULL(SUM(CASE WHEN l.EventType IN ('{AccessLogEvents.LoginFailed}', '{AccessLogEvents.AccessDenied}', '{AccessLogEvents.PortalLoginFailed}') THEN 1 ELSE 0 END), 0) AS FilteredFailed
                FROM dbo.AccessLogs l
                {WhereSql};

                SELECT l.*
                FROM dbo.AccessLogs l
                {WhereSql}
                {OrderBySql(query)}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, BuildParams(query));

            var total = await multi.ReadSingleAsync<int>();
            var counts = await multi.ReadSingleAsync<Counts>();
            var rows = (await multi.ReadAsync<AccessLog>()).ToList();

            return new AccessLogPageResult { Total = total, Filtered = counts.Filtered, FilteredFailed = counts.FilteredFailed, Rows = rows };
        }

        public async Task<List<AccessLog>> GetForExportAsync(AccessLogPageQuery query, int maxRows)
        {
            var sql = $@"
                SELECT TOP (@MaxRows) l.*
                FROM dbo.AccessLogs l
                {WhereSql}
                {OrderBySql(query)}";

            var p = new DynamicParameters(BuildParams(query));
            p.Add("MaxRows", maxRows);

            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<AccessLog>(sql, p)).ToList();
        }

        public async Task<List<string>> GetUsernamesAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            // Gabungan akun yang ada sekarang + yang pernah tercatat (akun yang sudah dihapus tetap bisa difilter)
            const string sql = @"
                SELECT Username FROM dbo.AdminUsers
                UNION
                SELECT DISTINCT Username FROM dbo.AccessLogs WHERE AdminUserId IS NOT NULL
                ORDER BY Username";
            return (await conn.QueryAsync<string>(sql)).ToList();
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteAsync("DELETE FROM dbo.AccessLogs WHERE CreatedAt < @Cutoff", new { Cutoff = cutoffUtc },
                commandTimeout: 120);
        }
    }
}
