using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models.ViewModels;

namespace FitToWorkApp.Repositories
{
    /// <summary>Rentang laporan FTW: batas UTC, filter department (null = semua), dan selisih zona waktu site (menit).</summary>
    public record FtwReportFilter(DateTime FromUtc, DateTime ToUtc, string? Department, int OffsetMinutes);

    /// <summary>
    /// Query laporan Fit To Work. HANYA MEMBACA (SELECT): tidak mengubah data maupun skema.
    /// Aturan hitung sama dengan dashboard: 1 karyawan dihitung 1x per hari lokal, status terakhir yang dipakai.
    /// Karyawan yang dihitung: karyawan AKP aktif (karyawan mitra tidak ikut).
    /// "Hari operasional" = tanggal yang ada minimal 1 pengisian (hari kosong seperti libur tidak menurunkan kepatuhan).
    /// Hari wajib per karyawan = hari operasional sejak karyawan terdaftar di sistem.
    /// </summary>
    public interface IFtwReportRepository
    {
        Task<List<FtwEmployeeRow>> GetEmployeeComplianceAsync(FtwReportFilter f);
        Task<List<FtwDailyRow>> GetDailyAsync(FtwReportFilter f);
        Task<List<FtwFitRow>> GetByJadwalAsync(FtwReportFilter f);
        Task<List<FtwFitRow>> GetByJamTidurAsync(FtwReportFilter f);
        Task<List<FtwNameCountRow>> GetUnfitBreakdownAsync(FtwReportFilter f);
        Task<int> GetOperationalDaysAsync(FtwReportFilter f);

        Task<List<FtwFollowUpRow>> GetUnfitEventsAsync(FtwReportFilter f, bool includeClinic, int max, bool includeTriage = false, bool includeOnline = false);

        /// <summary>True kalau tabel follow up online (dbo.FtwFollowUps) sudah ada di database.</summary>
        Task<bool> IsOnlineFollowUpAvailableAsync();

        /// <summary>True kalau tabel pemeriksaan awal (dbo.ClinicTriages, kolom FtwSubmissionId) sudah ada di database.</summary>
        Task<bool> IsTriageAvailableAsync();

        /// <summary>Pengisian tidak fit + jawaban screening + (opsional) hasil pemeriksaan klinik dalam 48 jam berikutnya.</summary>
        Task<List<FtwScreeningRow>> GetScreeningAsync(FtwReportFilter f, bool includeClinic, int max, bool includeOnline = false);
    }

    public class FtwReportRepository : IFtwReportRepository
    {
        private const string Local = "CAST(DATEADD(MINUTE, @Offset, s.CreatedAt) AS DATE)";
        private const string IsFit = "KesiapanKerja LIKE 'YA%'";
        private const string NotFit = "KesiapanKerja NOT LIKE 'YA%'";

        // sub = pengisian (1 baris per karyawan per hari, status terakhir) milik karyawan AKP aktif
        // ops = hari operasional, emp = karyawan yang dihitung (sesuai filter department)
        private const string Cte = $@"
            sub AS (
                SELECT s.EmployeeId, {Local} AS Tgl, s.JadwalKerja, s.JamTidur, s.KesiapanKerja, s.CreatedAt,
                       s.GejalaKesehatan, s.KualitasTidur, s.KondisiFokus, s.KonsumsiObat,
                       ROW_NUMBER() OVER (PARTITION BY s.EmployeeId, {Local} ORDER BY s.CreatedAt DESC) AS rn
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e0 ON e0.Id = s.EmployeeId AND e0.IsActive = 1 AND e0.MitraId IS NULL
                WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
            ),
            ops AS (SELECT DISTINCT Tgl FROM sub),
            emp AS (
                SELECT e.Id, e.NIP, e.Nama, d.Name AS Department, sc.Name AS Section,
                       CAST(DATEADD(MINUTE, @Offset, e.CreatedAt) AS DATE) AS Mulai
                FROM dbo.Employees e
                LEFT JOIN dbo.Departments d ON d.Id = e.DepartmentId
                LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
                WHERE e.IsActive = 1 AND e.MitraId IS NULL AND (@Dept IS NULL OR d.Name = @Dept)
            )";

        private readonly IDbConnectionFactory _connectionFactory;

        public FtwReportRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private static object P(FtwReportFilter f) => new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes };

        private async Task<List<T>> ListAsync<T>(string sql, object param)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<T>(sql, param)).ToList();
        }

        public Task<List<FtwEmployeeRow>> GetEmployeeComplianceAsync(FtwReportFilter f)
        {
            const string sql = $@"
                WITH {Cte},
                filled AS (
                    SELECT sub.EmployeeId, COUNT(1) AS Hari,
                           SUM(CASE WHEN sub.{NotFit} THEN 1 ELSE 0 END) AS TidakFit,
                           MAX(sub.Tgl) AS TerakhirTgl, MAX(sub.CreatedAt) AS TerakhirUtc
                    FROM sub
                    WHERE sub.rn = 1
                    GROUP BY sub.EmployeeId
                )
                SELECT emp.Id, emp.NIP, emp.Nama, ISNULL(emp.Department, '') AS Department, ISNULL(emp.Section, '') AS Section,
                       (SELECT COUNT(1) FROM ops WHERE ops.Tgl >= emp.Mulai) AS HariWajib,
                       ISNULL(f.Hari, 0) AS HariIsi,
                       ISNULL(f.TidakFit, 0) AS HariTidakFit,
                       f.TerakhirUtc,
                       (SELECT COUNT(1) FROM ops WHERE ops.Tgl >= emp.Mulai AND ops.Tgl > ISNULL(f.TerakhirTgl, '19000101')) AS BelumIsiBerturut
                FROM emp
                LEFT JOIN filled f ON f.EmployeeId = emp.Id
                ORDER BY emp.Nama";
            return ListAsync<FtwEmployeeRow>(sql, P(f));
        }

        public Task<List<FtwDailyRow>> GetDailyAsync(FtwReportFilter f)
        {
            const string sql = $@"
                WITH {Cte}
                SELECT sub.Tgl AS Tanggal, COUNT(1) AS Isi,
                       SUM(CASE WHEN sub.{IsFit} THEN 1 ELSE 0 END) AS Fit,
                       SUM(CASE WHEN sub.{NotFit} THEN 1 ELSE 0 END) AS TidakFit,
                       (SELECT COUNT(1) FROM emp WHERE emp.Mulai <= sub.Tgl) AS Target
                FROM sub
                INNER JOIN emp ON emp.Id = sub.EmployeeId
                WHERE sub.rn = 1
                GROUP BY sub.Tgl
                ORDER BY sub.Tgl";
            return ListAsync<FtwDailyRow>(sql, P(f));
        }

        public Task<int> GetOperationalDaysAsync(FtwReportFilter f)
        {
            const string sql = $@"WITH {Cte} SELECT COUNT(1) FROM ops";
            return ScalarAsync(sql, P(f));
        }

        private async Task<int> ScalarAsync(string sql, object param)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(sql, param);
        }

        public Task<List<FtwFitRow>> GetByJadwalAsync(FtwReportFilter f)
        {
            const string jadwal = "CASE WHEN sub.JadwalKerja LIKE 'NON%' THEN 'NON SHIFT' WHEN sub.JadwalKerja LIKE '%MALAM%' THEN 'SHIFT MALAM' ELSE 'SHIFT PAGI' END";
            const string sql = $@"
                WITH {Cte}
                SELECT {jadwal} AS Nama,
                       SUM(CASE WHEN sub.{IsFit} THEN 1 ELSE 0 END) AS Fit,
                       SUM(CASE WHEN sub.{NotFit} THEN 1 ELSE 0 END) AS TidakFit
                FROM sub
                INNER JOIN emp ON emp.Id = sub.EmployeeId
                WHERE sub.rn = 1
                GROUP BY {jadwal}
                ORDER BY {jadwal}";
            return ListAsync<FtwFitRow>(sql, P(f));
        }

        public Task<List<FtwFitRow>> GetByJamTidurAsync(FtwReportFilter f)
        {
            const string sql = $@"
                WITH {Cte}
                SELECT sub.JamTidur AS Nama,
                       SUM(CASE WHEN sub.{IsFit} THEN 1 ELSE 0 END) AS Fit,
                       SUM(CASE WHEN sub.{NotFit} THEN 1 ELSE 0 END) AS TidakFit
                FROM sub
                INNER JOIN emp ON emp.Id = sub.EmployeeId
                WHERE sub.rn = 1
                GROUP BY sub.JamTidur";
            return ListAsync<FtwFitRow>(sql, P(f));
        }

        public Task<List<FtwNameCountRow>> GetUnfitBreakdownAsync(FtwReportFilter f)
        {
            // Detail (gejala, kualitas tidur, fokus, obat) hanya ditanyakan kalau tidak fit; yang FIT disimpan "-".
            const string unfitFrom = "FROM sub INNER JOIN emp ON emp.Id = sub.EmployeeId WHERE sub.rn = 1 AND sub." + NotFit;
            const string sql = $@"
                WITH {Cte}
                SELECT 'Gejala' AS Grup, sub.GejalaKesehatan AS Nama, COUNT(1) AS Jumlah {unfitFrom} GROUP BY sub.GejalaKesehatan
                UNION ALL
                SELECT 'Kualitas', sub.KualitasTidur, COUNT(1) {unfitFrom} GROUP BY sub.KualitasTidur
                UNION ALL
                SELECT 'Fokus', sub.KondisiFokus, COUNT(1) {unfitFrom} GROUP BY sub.KondisiFokus
                UNION ALL
                SELECT 'Obat', sub.KonsumsiObat, COUNT(1) {unfitFrom} GROUP BY sub.KonsumsiObat";
            return ListAsync<FtwNameCountRow>(sql, P(f));
        }

        public Task<List<FtwScreeningRow>> GetScreeningAsync(FtwReportFilter f, bool includeClinic, int max, bool includeOnline = false)
        {
            var onlineCols = includeOnline
                ? "ol.Id AS OnlineId, ol.Hasil AS OnlineHasil, ol.IsClosed AS OnlineClosed, ol.NextFollowUpDate AS OnlineNext"
                : "CAST(NULL AS INT) AS OnlineId, CAST(NULL AS NVARCHAR(40)) AS OnlineHasil, CAST(NULL AS BIT) AS OnlineClosed, CAST(NULL AS DATE) AS OnlineNext";
            var onlineApply = includeOnline
                ? @"OUTER APPLY (
                        SELECT TOP 1 fu.Id, fu.Hasil, fu.IsClosed, fu.NextFollowUpDate
                        FROM dbo.FtwFollowUps fu
                        WHERE fu.FtwSubmissionId = s.Id
                        ORDER BY fu.FollowUpAt DESC, fu.Id DESC) ol"
                : "";
            var clinicCols = includeClinic
                ? "cv.VisitAt AS KlinikUtc, dg.Diagnosa, cv.Pemeriksaan, ob.Obat, cv.CatatanRekomendasi, cv.Rekomendasi, cv.HariIstirahat, cv.IstirahatDari, cv.IstirahatSampai"
                : @"CAST(NULL AS DATETIME2) AS KlinikUtc, CAST(NULL AS NVARCHAR(MAX)) AS Diagnosa, CAST(NULL AS NVARCHAR(1000)) AS Pemeriksaan,
                    CAST(NULL AS NVARCHAR(MAX)) AS Obat, CAST(NULL AS NVARCHAR(1000)) AS CatatanRekomendasi, CAST(NULL AS NVARCHAR(30)) AS Rekomendasi,
                    CAST(NULL AS INT) AS HariIstirahat, CAST(NULL AS DATE) AS IstirahatDari, CAST(NULL AS DATE) AS IstirahatSampai";
            var clinicApply = includeClinic
                ? @"OUTER APPLY (
                        SELECT TOP 1 v.Id, v.VisitAt, v.Pemeriksaan, v.Rekomendasi, v.HariIstirahat, v.IstirahatDari, v.IstirahatSampai, v.CatatanRekomendasi
                        FROM dbo.ClinicVisits v
                        WHERE v.EmployeeId = s.EmployeeId AND v.IsCancelled = 0
                          AND v.VisitAt >= s.CreatedAt AND v.VisitAt < DATEADD(HOUR, 48, s.CreatedAt)
                        ORDER BY v.VisitAt) cv
                    OUTER APPLY (
                        SELECT STRING_AGG(dz.Name, ', ') WITHIN GROUP (ORDER BY dz.Name) AS Diagnosa
                        FROM dbo.ClinicVisitDiseases vd
                        INNER JOIN dbo.Diseases dz ON dz.Id = vd.DiseaseId
                        WHERE vd.ClinicVisitId = cv.Id) dg
                    OUTER APPLY (
                        SELECT STRING_AGG(CAST(m.NamaSnapshot + ' ' + CAST(m.Qty AS NVARCHAR(10)) + ' ' + m.SatuanSnapshot
                                    + CASE WHEN m.AturanPakai IS NULL OR m.AturanPakai = '' THEN '' ELSE ' (' + m.AturanPakai + ')' END AS NVARCHAR(MAX)), '; ') AS Obat
                        FROM dbo.ClinicVisitMedicines m
                        WHERE m.ClinicVisitId = cv.Id) ob"
                : "";

            var sql = $@"
                SELECT TOP (@Max)
                       s.Id, s.EmployeeId, e.NIP, s.NamaSnapshot AS Nama,
                       ISNULL(NULLIF(sc.Name, ''), s.SectionSnapshot) AS Section, ISNULL(d.Name, '') AS Department,
                       s.JadwalKerja,
                       CASE WHEN s.AreaKerja = 'Other' AND s.AreaKerjaLainnya IS NOT NULL AND s.AreaKerjaLainnya <> '' THEN s.AreaKerjaLainnya ELSE s.AreaKerja END AS AreaKerja,
                       s.JamTidur, s.KualitasTidur, s.GejalaKesehatan, s.KondisiFokus, s.KonsumsiObat, s.CreatedAt AS WaktuUtc,
                       {clinicCols},
                       {onlineCols}
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId AND e.MitraId IS NULL
                LEFT JOIN dbo.Departments d ON d.Id = e.DepartmentId
                LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
                {clinicApply}
                {onlineApply}
                WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                  AND s.{NotFit}
                  AND (@Dept IS NULL OR d.Name = @Dept)
                ORDER BY s.CreatedAt DESC";
            return ListAsync<FtwScreeningRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Max = max });
        }

        public async Task<bool> IsTriageAvailableAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN OBJECT_ID('dbo.ClinicTriages', 'U') IS NOT NULL AND COL_LENGTH('dbo.ClinicTriages', 'FtwSubmissionId') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        }

        public async Task<bool> IsOnlineFollowUpAvailableAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID('dbo.FtwFollowUps', 'U') IS NOT NULL AND COL_LENGTH('dbo.FtwFollowUps', 'IsClosed') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        }

        public Task<List<FtwFollowUpRow>> GetUnfitEventsAsync(FtwReportFilter f, bool includeClinic, int max, bool includeTriage = false, bool includeOnline = false)
        {
            var onlineCols = includeOnline
                ? "ol.Id AS OnlineId, ol.Hasil AS OnlineHasil, ol.Kanal AS OnlineKanal, ol.FollowUpAt AS OnlineAtUtc, ol.ByName AS OnlineBy, ol.Catatan AS OnlineCatatan, ISNULL(ol.Jumlah, 0) AS OnlineCount, ol.IsClosed AS OnlineClosed, ol.NextFollowUpDate AS OnlineNext"
                : "CAST(NULL AS INT) AS OnlineId, CAST(NULL AS NVARCHAR(40)) AS OnlineHasil, CAST(NULL AS NVARCHAR(30)) AS OnlineKanal, CAST(NULL AS DATETIME2) AS OnlineAtUtc, CAST(NULL AS NVARCHAR(150)) AS OnlineBy, CAST(NULL AS NVARCHAR(1000)) AS OnlineCatatan, CAST(0 AS INT) AS OnlineCount, CAST(NULL AS BIT) AS OnlineClosed, CAST(NULL AS DATE) AS OnlineNext";
            var onlineApply = includeOnline
                ? @"OUTER APPLY (
                        SELECT TOP 1 fu.Id, fu.Hasil, fu.Kanal, fu.FollowUpAt, fu.ByName, fu.Catatan, fu.IsClosed, fu.NextFollowUpDate,
                               (SELECT COUNT(1) FROM dbo.FtwFollowUps fu2 WHERE fu2.FtwSubmissionId = s.Id) AS Jumlah
                        FROM dbo.FtwFollowUps fu
                        WHERE fu.FtwSubmissionId = s.Id
                        ORDER BY fu.FollowUpAt DESC, fu.Id DESC) ol"
                : "";
            var triageCols = includeTriage
                ? "tr.Id AS TriageId, tr.Status AS TriageStatus, tr.VisitId AS TriageVisitId"
                : "CAST(NULL AS INT) AS TriageId, CAST(NULL AS NVARCHAR(20)) AS TriageStatus, CAST(NULL AS INT) AS TriageVisitId";
            var triageApply = includeTriage
                ? @"OUTER APPLY (
                        SELECT TOP 1 t.Id, t.Status, t.VisitId
                        FROM dbo.ClinicTriages t
                        WHERE t.FtwSubmissionId = s.Id AND t.Status IN ('Draft', 'Selesai')
                        ORDER BY t.Id DESC) tr"
                : "";
            // Setiap pengisian TIDAK FIT dalam rentang = 1 kejadian (bukan hanya status akhir hari), karena tiap kejadian perlu ditindaklanjuti.
            // Tindak lanjut diturunkan dari data yang sudah ada: kunjungan klinik dalam 48 jam berikutnya (kalau boleh dilihat)
            // dan pengisian ulang berstatus FIT dalam 24 jam berikutnya.
            var clinicCols = includeClinic
                ? "cv.VisitAt AS KlinikUtc, cv.Rekomendasi AS KlinikRekomendasi, cv.HariIstirahat AS KlinikHariIstirahat"
                : "CAST(NULL AS DATETIME2) AS KlinikUtc, CAST(NULL AS NVARCHAR(30)) AS KlinikRekomendasi, CAST(NULL AS INT) AS KlinikHariIstirahat";
            var clinicApply = includeClinic
                ? @"OUTER APPLY (
                        SELECT TOP 1 v.VisitAt, v.Rekomendasi, v.HariIstirahat
                        FROM dbo.ClinicVisits v
                        WHERE v.EmployeeId = s.EmployeeId AND v.IsCancelled = 0
                          AND v.VisitAt >= s.CreatedAt AND v.VisitAt < DATEADD(HOUR, 48, s.CreatedAt)
                        ORDER BY v.VisitAt) cv"
                : "";

            var sql = $@"
                SELECT TOP (@Max)
                       s.Id, s.EmployeeId, e.NIP, s.NamaSnapshot AS Nama,
                       ISNULL(d.Name, '') AS Department, ISNULL(sc.Name, s.SectionSnapshot) AS Section,
                       s.JadwalKerja, s.NomorHp, s.AreaKerja, s.CreatedAt AS WaktuUtc,
                       s.GejalaKesehatan, s.JamTidur, s.KualitasTidur, s.KondisiFokus, s.KonsumsiObat,
                       (SELECT MIN(s2.CreatedAt) FROM dbo.FitToWorkSubmissions s2
                        WHERE s2.EmployeeId = s.EmployeeId AND s2.CreatedAt > s.CreatedAt
                          AND s2.CreatedAt <= DATEADD(HOUR, 24, s.CreatedAt) AND s2.{IsFit}) AS IsiUlangFitUtc,
                       (SELECT COUNT(DISTINCT CAST(DATEADD(MINUTE, @Offset, s3.CreatedAt) AS DATE))
                        FROM dbo.FitToWorkSubmissions s3
                        WHERE s3.EmployeeId = s.EmployeeId AND s3.{NotFit}
                          AND s3.CreatedAt <= s.CreatedAt AND s3.CreatedAt > DATEADD(DAY, -7, s.CreatedAt)) AS TidakFit7Hari,
                       {clinicCols},
                       {triageCols},
                       {onlineCols}
                FROM dbo.FitToWorkSubmissions s
                INNER JOIN dbo.Employees e ON e.Id = s.EmployeeId AND e.MitraId IS NULL
                LEFT JOIN dbo.Departments d ON d.Id = e.DepartmentId
                LEFT JOIN dbo.Sections sc ON sc.Id = e.SectionId
                {clinicApply}
                {triageApply}
                {onlineApply}
                WHERE s.CreatedAt >= @FromUtc AND s.CreatedAt < @ToUtc
                  AND s.{NotFit}
                  AND (@Dept IS NULL OR d.Name = @Dept)
                ORDER BY s.CreatedAt DESC";
            return ListAsync<FtwFollowUpRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Max = max });
        }
    }
}
