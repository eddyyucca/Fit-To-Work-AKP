using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models.ViewModels;

namespace FitToWorkApp.Repositories
{
    /// <summary>
    /// Query khusus dashboard. Semua parameter waktu dikirim dalam UTC (sama seperti kolom CreatedAt),
    /// dan pengelompokan per tanggal memakai @Offset (menit) supaya tanggalnya mengikuti jam site.
    /// Aturan hitung: 1 karyawan dihitung 1x per hari; kalau isi ulang, yang dipakai status terakhir.
    /// </summary>
    public interface IDashboardRepository
    {
        Task<DashboardTodaySummary> GetTodaySummaryAsync(DateTime fromUtc, DateTime toUtc);
        Task<IEnumerable<DashboardDailyRow>> GetDailyTrendAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes);
        Task<DashboardCoverageRow> GetCoverageAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes);
        Task<IEnumerable<DashboardTopRow>> GetTopRegistrantsAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes, int top);
        Task<IEnumerable<DashboardSubmissionRow>> GetRecentAsync(DateTime fromUtc, DateTime toUtc, int take);
        Task<DashboardSubmissionRow?> GetSubmissionAsync(int submissionId);
        Task<IEnumerable<DashboardSectionRow>> GetSectionAchievementAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes);
        Task<IEnumerable<DashboardSectionEmployeeRow>> GetSectionEmployeesAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes, int? sectionId);
    }

    public class DashboardRepository : IDashboardRepository
    {
        // Kategori jadwal kerja mengikuti master data FormOptions: NON SHIFT / SHIFT PAGI / SHIFT MALAM.
        // Nilai lain di luar tiga itu (kalau nanti ditambah) otomatis masuk "SHIFT PAGI".
        private const string IsNonShift = "s.JadwalKerja LIKE 'NON%'";
        private const string IsShiftMalam = "s.JadwalKerja LIKE '%MALAM%'";
        private const string IsFit = "s.KesiapanKerja LIKE 'YA%'";
        private const string LocalDate = "CAST(DATEADD(MINUTE, @Offset, s.CreatedAt) AS DATE)";

        private readonly IDbConnectionFactory _connectionFactory;

        public DashboardRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<DashboardTodaySummary> GetTodaySummaryAsync(DateTime fromUtc, DateTime toUtc)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                WITH latest AS (
                    SELECT CASE WHEN {IsFit} THEN 1 ELSE 0 END AS IsFit,
                           ROW_NUMBER() OVER (PARTITION BY s.EmployeeId ORDER BY s.CreatedAt DESC) AS rn
                    FROM dbo.FitToWorkSubmissions s
                    INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId AND e.IsActive = 1
                    WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                )
                SELECT
                    (SELECT COUNT(1) FROM dbo.Employees WHERE IsActive = 1 AND MitraId IS NULL) AS TotalKaryawan,
                    COUNT(1)                    AS SudahIsi,
                    ISNULL(SUM(IsFit), 0)       AS Fit,
                    ISNULL(SUM(1 - IsFit), 0)   AS TidakFit
                FROM latest
                WHERE rn = 1";

            return await conn.QuerySingleAsync<DashboardTodaySummary>(sql, new { FromUtc = fromUtc, ToUtc = toUtc });
        }

        public async Task<IEnumerable<DashboardDailyRow>> GetDailyTrendAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                WITH d AS (
                    SELECT {LocalDate} AS Tanggal,
                           CASE WHEN {IsNonShift} THEN 'NON' WHEN {IsShiftMalam} THEN 'MALAM' ELSE 'PAGI' END AS Jadwal,
                           CASE WHEN {IsFit} THEN 1 ELSE 0 END AS IsFit,
                           ROW_NUMBER() OVER (PARTITION BY s.EmployeeId, {LocalDate} ORDER BY s.CreatedAt DESC) AS rn
                    FROM dbo.FitToWorkSubmissions s
                    WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                )
                SELECT Tanggal,
                       COUNT(1)                                        AS Total,
                       SUM(CASE WHEN Jadwal = 'NON'   THEN 1 ELSE 0 END) AS NonShift,
                       SUM(CASE WHEN Jadwal = 'PAGI'  THEN 1 ELSE 0 END) AS ShiftPagi,
                       SUM(CASE WHEN Jadwal = 'MALAM' THEN 1 ELSE 0 END) AS ShiftMalam,
                       SUM(IsFit)                                      AS Fit,
                       SUM(1 - IsFit)                                  AS TidakFit
                FROM d
                WHERE rn = 1
                GROUP BY Tanggal
                ORDER BY Tanggal";

            return await conn.QueryAsync<DashboardDailyRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offsetMinutes });
        }

        public async Task<DashboardCoverageRow> GetCoverageAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes)
        {
            using var conn = _connectionFactory.CreateConnection();
            // "Hari operasional" = tanggal yang ada minimal 1 pengisian, jadi hari libur / Minggu
            // yang memang kosong tidak ikut menurunkan persentase.
            const string sql = $@"
                WITH d AS (
                    SELECT DISTINCT {LocalDate} AS Tanggal, s.EmployeeId
                    FROM dbo.FitToWorkSubmissions s
                    INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId AND e.IsActive = 1
                    WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                )
                SELECT
                    (SELECT COUNT(1) FROM dbo.Employees WHERE IsActive = 1 AND MitraId IS NULL) AS TotalKaryawan,
                    (SELECT COUNT(DISTINCT Tanggal) FROM d)                 AS HariOperasional,
                    (SELECT COUNT(1) FROM d)                                AS SudahIsi";

            return await conn.QuerySingleAsync<DashboardCoverageRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offsetMinutes });
        }

        public async Task<IEnumerable<DashboardTopRow>> GetTopRegistrantsAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                SELECT TOP (@Top)
                       e.NIP,
                       e.Nama,
                       ps.Name AS Jabatan,
                       sc.Name AS Section,
                       COUNT(DISTINCT {LocalDate})                          AS JumlahHari,
                       SUM(CASE WHEN {IsFit} THEN 0 ELSE 1 END)            AS TidakFit,
                       MAX(s.CreatedAt)                                     AS TerakhirUtc
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId AND e.IsActive = 1
                LEFT JOIN dbo.Positions ps ON ps.Id = e.PositionId
                LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
                WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                GROUP BY e.Id, e.NIP, e.Nama, ps.Name, sc.Name
                ORDER BY JumlahHari DESC, MAX(s.CreatedAt) DESC";

            return await conn.QueryAsync<DashboardTopRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offsetMinutes, Top = top });
        }

        private const string SubmissionSelect = $@"
                SELECT s.Id,
                       e.NIP,
                       s.NamaSnapshot    AS Nama,
                       ps.Name           AS Jabatan,
                       s.SectionSnapshot AS Section,
                       s.JadwalKerja,
                       CASE WHEN {IsFit} THEN 'FIT' ELSE 'TIDAK FIT' END AS Status,
                       s.CreatedAt       AS CreatedAtUtc
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId
                LEFT JOIN dbo.Positions ps ON ps.Id = e.PositionId";

        public async Task<IEnumerable<DashboardSubmissionRow>> GetRecentAsync(DateTime fromUtc, DateTime toUtc, int take)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                SELECT TOP (@Take) * FROM (
                    {SubmissionSelect}
                    WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                ) x
                ORDER BY x.CreatedAtUtc DESC";

            return await conn.QueryAsync<DashboardSubmissionRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Take = take });
        }

        public async Task<DashboardSubmissionRow?> GetSubmissionAsync(int submissionId)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"{SubmissionSelect}
                WHERE s.Id = @Id";

            return await conn.QueryFirstOrDefaultAsync<DashboardSubmissionRow>(sql, new { Id = submissionId });
        }

        // ---------------- Pengisian FTW per Section ----------------

        public async Task<IEnumerable<DashboardSectionRow>> GetSectionAchievementAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes)
        {
            using var conn = _connectionFactory.CreateConnection();
            // Karyawan dikelompokkan menurut section SAAT INI di Data Karyawan (bukan snapshot di pengisian),
            // jadi "sudah isi" tidak pernah melebihi jumlah karyawan section itu. Hanya karyawan AKP aktif.
            const string sql = $@"
                WITH emp AS (
                    SELECT e.Id, e.SectionId
                    FROM dbo.Employees e
                    WHERE e.IsActive = 1 AND e.MitraId IS NULL
                ),
                d AS (
                    SELECT s.EmployeeId,
                           CASE WHEN {IsFit} THEN 1 ELSE 0 END AS IsFit,
                           ROW_NUMBER() OVER (PARTITION BY s.EmployeeId, {LocalDate} ORDER BY s.CreatedAt DESC) AS rn
                    FROM dbo.FitToWorkSubmissions s
                    WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                ),
                per AS (
                    SELECT emp.Id, emp.SectionId,
                           COUNT(d.EmployeeId)    AS HariIsi,
                           ISNULL(SUM(d.IsFit), 0) AS Fit
                    FROM emp
                    LEFT JOIN d ON d.EmployeeId = emp.Id AND d.rn = 1
                    GROUP BY emp.Id, emp.SectionId
                )
                SELECT per.SectionId,
                       sc.Name                                            AS Section,
                       dp.Name                                            AS Department,
                       COUNT(1)                                           AS JumlahKaryawan,
                       SUM(per.HariIsi)                                   AS SudahIsi,
                       SUM(per.Fit)                                       AS Fit,
                       SUM(per.HariIsi) - SUM(per.Fit)                    AS TidakFit,
                       SUM(CASE WHEN per.HariIsi > 0 THEN 1 ELSE 0 END)   AS KaryawanPernahIsi
                FROM per
                LEFT JOIN dbo.Sections sc ON sc.Id = per.SectionId
                LEFT JOIN dbo.Departments dp ON dp.Id = sc.DepartmentId
                GROUP BY per.SectionId, sc.Name, dp.Name";

            return await conn.QueryAsync<DashboardSectionRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offsetMinutes });
        }

        public async Task<IEnumerable<DashboardSectionEmployeeRow>> GetSectionEmployeesAsync(DateTime fromUtc, DateTime toUtc, int offsetMinutes, int? sectionId)
        {
            using var conn = _connectionFactory.CreateConnection();
            // sectionId NULL = karyawan yang belum punya section ("(Tanpa Section)")
            const string sql = $@"
                SELECT e.NIP,
                       e.Nama,
                       ps.Name AS Jabatan,
                       (SELECT COUNT(DISTINCT {LocalDate})
                        FROM dbo.FitToWorkSubmissions s
                        WHERE s.EmployeeId = e.Id AND s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc) AS HariIsi,
                       (SELECT MAX(s.CreatedAt) FROM dbo.FitToWorkSubmissions s WHERE s.EmployeeId = e.Id) AS TerakhirUtc
                FROM dbo.Employees e
                LEFT JOIN dbo.Positions ps ON ps.Id = e.PositionId
                WHERE e.IsActive = 1 AND e.MitraId IS NULL
                  AND ((@SectionId IS NULL AND e.SectionId IS NULL) OR e.SectionId = @SectionId)
                ORDER BY e.Nama";

            return await conn.QueryAsync<DashboardSectionEmployeeRow>(sql,
                new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offsetMinutes, SectionId = sectionId });
        }
    }
}
