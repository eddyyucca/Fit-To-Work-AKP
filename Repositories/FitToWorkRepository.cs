using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;

namespace FitToWorkApp.Repositories
{
    /// <summary>Parameter untuk ambil riwayat pengisian per halaman (server-side DataTables).</summary>
    public class HistoryPageQuery
    {
        public string? Search { get; set; }          // cari di NIP / nama / section
        public DateTime? FromUtc { get; set; }       // batas bawah CreatedAt (UTC, inklusif)
        public DateTime? ToUtc { get; set; }         // batas atas CreatedAt (UTC, eksklusif)
        public string? Section { get; set; }
        public string? JadwalKerja { get; set; }
        public bool? IsFit { get; set; }             // null = semua, true = FIT, false = TIDAK FIT
        public int SortColumn { get; set; }          // index kolom tabel (lihat HistorySortColumns)
        public bool SortDesc { get; set; } = true;
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
    }

    public class HistoryPageResult
    {
        public int Total { get; set; }               // total semua riwayat (tanpa filter)
        public int Filtered { get; set; }            // total setelah filter
        public int FilteredTidakFit { get; set; }    // berapa yang TIDAK FIT dari hasil filter
        public List<FitToWorkSubmission> Rows { get; set; } = new();
    }

    public interface IFitToWorkRepository
    {
        Task<int> CreateAsync(FitToWorkSubmission submission);
        Task<IEnumerable<FitToWorkSubmission>> SearchAsync(HistoryFilterViewModel filter);
        Task<HistoryPageResult> GetPageAsync(HistoryPageQuery query);
        Task<List<FitToWorkSubmission>> GetForExportAsync(HistoryPageQuery query, int maxRows);
        Task<FitToWorkSubmission?> GetByIdAsync(int id);
        /// <summary>Pengisian terakhir karyawan (tanpa batas tanggal). Periode dihitung di FtwRules.</summary>
        Task<FitToWorkSubmission?> GetLatestForEmployeeAsync(int employeeId);
        Task<List<FitToWorkSubmission>> GetByEmployeeAsync(int employeeId, int top);
    }

    public class FitToWorkRepository : IFitToWorkRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public FitToWorkRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> CreateAsync(FitToWorkSubmission submission)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.FitToWorkSubmissions
                    (EmployeeId, NamaSnapshot, SectionSnapshot, NomorHp, JadwalKerja, AreaKerja, AreaKerjaLainnya,
                     JamTidur, KualitasTidur, GejalaKesehatan, KondisiFokus, KonsumsiObat, KesiapanKerja, CreatedAt,
                     LokasiSiteId, LokasiNama, GpsLat, GpsLng, GpsAkurasi, JarakMeter, LokasiVia)
                OUTPUT INSERTED.Id
                VALUES
                    (@EmployeeId, @NamaSnapshot, @SectionSnapshot, @NomorHp, @JadwalKerja, @AreaKerja, @AreaKerjaLainnya,
                     @JamTidur, @KualitasTidur, @GejalaKesehatan, @KondisiFokus, @KonsumsiObat, @KesiapanKerja, SYSUTCDATETIME(),
                     @LokasiSiteId, @LokasiNama, @GpsLat, @GpsLng, @GpsAkurasi, @JarakMeter, @LokasiVia)";
            return await conn.ExecuteScalarAsync<int>(sql, submission);
        }

        public async Task<IEnumerable<FitToWorkSubmission>> SearchAsync(HistoryFilterViewModel filter)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT s.*, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                WHERE (@NIP IS NULL OR e.NIP LIKE '%' + @NIP + '%')
                  AND (@Nama IS NULL OR s.NamaSnapshot LIKE '%' + @Nama + '%')
                  AND (@Section IS NULL OR s.SectionSnapshot = @Section)
                  AND (@KesiapanKerja IS NULL OR s.KesiapanKerja = @KesiapanKerja)
                  AND (@TanggalMulai IS NULL OR s.CreatedAt >= @TanggalMulai)
                  AND (@TanggalSelesai IS NULL OR s.CreatedAt < DATEADD(DAY, 1, @TanggalSelesai))
                ORDER BY s.CreatedAt DESC";

            return await conn.QueryAsync<FitToWorkSubmission>(sql, new
            {
                filter.NIP,
                filter.Nama,
                filter.Section,
                filter.KesiapanKerja,
                filter.TanggalMulai,
                filter.TanggalSelesai
            });
        }

        // Urutan HARUS sama dengan urutan <th> di Views/History/Index.cshtml (whitelist, aman dari SQL injection)
        private static readonly string[] HistorySortColumns =
        {
            "s.CreatedAt",        // 0 Waktu
            "e.NIP",              // 1
            "s.NamaSnapshot",     // 2 Nama
            "s.SectionSnapshot",  // 3 Section
            "s.JadwalKerja",      // 4 Jadwal
            "s.KesiapanKerja"     // 5 Kesiapan
        };

        private const string HistoryWhereSql = @"
            WHERE (@FromUtc IS NULL OR s.CreatedAt >= @FromUtc)
              AND (@ToUtc   IS NULL OR s.CreatedAt <  @ToUtc)
              AND (@Section IS NULL OR s.SectionSnapshot = @Section)
              AND (@JadwalKerja IS NULL OR s.JadwalKerja = @JadwalKerja)
              AND (@IsFit IS NULL
                   OR (@IsFit = 1 AND s.KesiapanKerja LIKE 'YA%')
                   OR (@IsFit = 0 AND s.KesiapanKerja NOT LIKE 'YA%'))
              AND (@Like IS NULL
                   OR e.NIP LIKE @Like OR s.NamaSnapshot LIKE @Like OR s.SectionSnapshot LIKE @Like)";

        private class HistoryCounts
        {
            public int Filtered { get; set; }
            public int FilteredTidakFit { get; set; }
        }

        public async Task<HistoryPageResult> GetPageAsync(HistoryPageQuery query)
        {
            var sortColumn = HistorySortColumns[Math.Clamp(query.SortColumn, 0, HistorySortColumns.Length - 1)];
            var sortDir = query.SortDesc ? "DESC" : "ASC";

            string? like = null;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var escaped = query.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
                like = "%" + escaped + "%";
            }

            // Kolom yang diambil sengaja dibatasi (bukan s.*) karena tabel hanya butuh ini;
            // isian lengkap diambil terpisah saat tombol Detail diklik.
            var sql = $@"
                SELECT COUNT(1) FROM dbo.FitToWorkSubmissions;

                SELECT COUNT(1) AS Filtered,
                       ISNULL(SUM(CASE WHEN s.KesiapanKerja LIKE 'YA%' THEN 0 ELSE 1 END), 0) AS FilteredTidakFit
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                {HistoryWhereSql};

                SELECT s.Id, s.EmployeeId, s.NamaSnapshot, s.SectionSnapshot, s.JadwalKerja,
                       s.AreaKerja, s.KesiapanKerja, s.CreatedAt, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                {HistoryWhereSql}
                ORDER BY {sortColumn} {sortDir}, s.Id {sortDir}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, new
            {
                query.FromUtc,
                query.ToUtc,
                Section = string.IsNullOrWhiteSpace(query.Section) ? null : query.Section,
                JadwalKerja = string.IsNullOrWhiteSpace(query.JadwalKerja) ? null : query.JadwalKerja,
                query.IsFit,
                Like = like,
                Skip = Math.Max(query.Skip, 0),
                Take = Math.Clamp(query.Take, 1, 100)
            });

            var total = await multi.ReadSingleAsync<int>();
            var counts = await multi.ReadSingleAsync<HistoryCounts>();
            var rows = (await multi.ReadAsync<FitToWorkSubmission>()).ToList();

            return new HistoryPageResult
            {
                Total = total,
                Filtered = counts.Filtered,
                FilteredTidakFit = counts.FilteredTidakFit,
                Rows = rows
            };
        }

        // Semua kolom (lengkap) untuk export Excel, dengan filter & urutan yang SAMA seperti tabel di layar.
        // Dibatasi maxRows supaya file & pemakaian memori server tetap wajar.
        public async Task<List<FitToWorkSubmission>> GetForExportAsync(HistoryPageQuery query, int maxRows)
        {
            var sortColumn = HistorySortColumns[Math.Clamp(query.SortColumn, 0, HistorySortColumns.Length - 1)];
            var sortDir = query.SortDesc ? "DESC" : "ASC";

            string? like = null;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var escaped = query.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
                like = "%" + escaped + "%";
            }

            var sql = $@"
                SELECT TOP (@MaxRows) s.*, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                {HistoryWhereSql}
                ORDER BY {sortColumn} {sortDir}, s.Id {sortDir}";

            using var conn = _connectionFactory.CreateConnection();
            var rows = await conn.QueryAsync<FitToWorkSubmission>(sql, new
            {
                query.FromUtc,
                query.ToUtc,
                Section = string.IsNullOrWhiteSpace(query.Section) ? null : query.Section,
                JadwalKerja = string.IsNullOrWhiteSpace(query.JadwalKerja) ? null : query.JadwalKerja,
                query.IsFit,
                Like = like,
                MaxRows = maxRows
            });
            return rows.ToList();
        }

        public async Task<FitToWorkSubmission?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT s.*, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                WHERE s.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<FitToWorkSubmission>(sql, new { Id = id });
        }

        // Riwayat milik satu karyawan (portal "Riwayat Saya"), terbaru dulu
        public async Task<List<FitToWorkSubmission>> GetByEmployeeAsync(int employeeId, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT TOP (@Top) s.*, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                WHERE s.EmployeeId = @EmployeeId
                ORDER BY s.CreatedAt DESC";
            return (await conn.QueryAsync<FitToWorkSubmission>(sql, new { EmployeeId = employeeId, Top = top })).ToList();
        }

        public async Task<FitToWorkSubmission?> GetLatestForEmployeeAsync(int employeeId)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT TOP 1 s.*, e.NIP
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                WHERE s.EmployeeId = @EmployeeId
                ORDER BY s.CreatedAt DESC";
            return await conn.QueryFirstOrDefaultAsync<FitToWorkSubmission>(sql, new { EmployeeId = employeeId });
        }
    }
}
