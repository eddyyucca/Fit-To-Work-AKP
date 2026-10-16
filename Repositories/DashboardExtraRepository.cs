using Dapper;
using FitToWorkApp.Data;

namespace FitToWorkApp.Repositories
{
    // ---------- Klinik ----------
    public class ClinicDashboardSummary
    {
        public int TotalKunjungan { get; set; }
        public int KaryawanUnik { get; set; }
        public int KembaliBekerja { get; set; }
        public int KerjaTerbatas { get; set; }
        public int Istirahat { get; set; }
        public int TotalHariIstirahat { get; set; }
        public int Rujuk { get; set; }
        public int ObatKeluar { get; set; }
    }

    public class DashboardBucketRow
    {
        public int Bucket { get; set; }        // jam (0-23) atau selisih hari dari tanggal awal
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
    }

    public class DashboardNameCountRow
    {
        public string Nama { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
    }

    // ---------- Aktivitas Kebugaran (dulu Gym) ----------
    public class GymDashboardSummary
    {
        public int TotalKunjungan { get; set; }
        public int KaryawanUnik { get; set; }
        public int TotalKaryawanAktif { get; set; }
        public decimal? RataDurasiMenit { get; set; }   // hanya sesi yang diselesaikan sendiri
        public int TidakCheckout { get; set; }
        public long TotalJarakMeter { get; set; }       // jarak GPS sesi yang diselesaikan
        public int SedangDiGym { get; set; }            // saat ini (tidak tergantung periode)
    }

    public class GymTopRow
    {
        public int EmployeeId { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int? TotalMenit { get; set; }
        public long? TotalJarakMeter { get; set; }
        public DateTime TerakhirUtc { get; set; }
        public decimal? BeratTerakhir { get; set; }
    }

    public class GymActivityRow
    {
        public int? ActivityTypeId { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string Ikon { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int Karyawan { get; set; }
        public decimal? RataDurasiMenit { get; set; }
        public long TotalJarakMeter { get; set; }
    }

    /// <summary>
    /// Query dashboard untuk Klinik & Gym. Parameter waktu dalam UTC; pengelompokan per jam/tanggal
    /// memakai @Offset (menit) supaya mengikuti jam site. Kunjungan klinik yang dibatalkan tidak dihitung.
    /// </summary>
    public interface IDashboardExtraRepository
    {
        Task<ClinicDashboardSummary> GetClinicSummaryAsync(DateTime fromUtc, DateTime toUtc);
        /// <param name="hourly">true = per jam (0-23), false = per hari (selisih hari dari <paramref name="fromLocalDate"/>)</param>
        Task<List<DashboardBucketRow>> GetClinicTrendAsync(DateTime fromUtc, DateTime toUtc, int offset, bool hourly, DateTime fromLocalDate);
        Task<List<DashboardNameCountRow>> GetTopDiseasesAsync(DateTime fromUtc, DateTime toUtc, int top);
        Task<List<DashboardNameCountRow>> GetTopClinicDepartmentsAsync(DateTime fromUtc, DateTime toUtc, int top);

        Task<GymDashboardSummary> GetGymSummaryAsync(DateTime fromUtc, DateTime toUtc);
        Task<List<DashboardBucketRow>> GetGymPerHourAsync(DateTime fromUtc, DateTime toUtc, int offset);
        Task<List<GymTopRow>> GetGymTopAsync(DateTime fromUtc, DateTime toUtc, int top);
        Task<List<GymActivityRow>> GetGymPerActivityAsync(DateTime fromUtc, DateTime toUtc);
    }

    public class DashboardExtraRepository : IDashboardExtraRepository
    {
        private const string VisitRange = "v.IsCancelled = 0 AND v.VisitAt >= @FromUtc AND v.VisitAt < @ToUtc";

        private readonly IDbConnectionFactory _connectionFactory;

        public DashboardExtraRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ClinicDashboardSummary> GetClinicSummaryAsync(DateTime fromUtc, DateTime toUtc)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                SELECT COUNT(1) AS TotalKunjungan,
                       COUNT(DISTINCT v.EmployeeId) AS KaryawanUnik,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'KembaliBekerja' THEN 1 ELSE 0 END), 0) AS KembaliBekerja,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'KerjaTerbatas' THEN 1 ELSE 0 END), 0) AS KerjaTerbatas,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN 1 ELSE 0 END), 0) AS Istirahat,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN ISNULL(v.HariIstirahat, 0) ELSE 0 END), 0) AS TotalHariIstirahat,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Rujuk' THEN 1 ELSE 0 END), 0) AS Rujuk,
                       (SELECT ISNULL(SUM(m.Qty), 0)
                        FROM dbo.ClinicVisitMedicines m
                        INNER JOIN dbo.ClinicVisits v2 ON v2.Id = m.ClinicVisitId
                        WHERE v2.IsCancelled = 0 AND v2.VisitAt >= @FromUtc AND v2.VisitAt < @ToUtc) AS ObatKeluar
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}";
            return await conn.QuerySingleAsync<ClinicDashboardSummary>(sql, new { FromUtc = fromUtc, ToUtc = toUtc });
        }

        public async Task<List<DashboardBucketRow>> GetClinicTrendAsync(DateTime fromUtc, DateTime toUtc, int offset, bool hourly, DateTime fromLocalDate)
        {
            var bucket = hourly
                ? "DATEPART(HOUR, DATEADD(MINUTE, @Offset, v.VisitAt))"
                : "DATEDIFF(DAY, @FromLocal, CAST(DATEADD(MINUTE, @Offset, v.VisitAt) AS DATE))";
            var sql = $@"
                SELECT {bucket} AS Bucket, COUNT(1) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}
                GROUP BY {bucket}";
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<DashboardBucketRow>(sql,
                new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offset, FromLocal = fromLocalDate.Date })).ToList();
        }

        public async Task<List<DashboardNameCountRow>> GetTopDiseasesAsync(DateTime fromUtc, DateTime toUtc, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                SELECT TOP (@Top) d.Name AS Nama, COUNT(1) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisitDiseases vd
                INNER JOIN dbo.ClinicVisits v ON v.Id = vd.ClinicVisitId
                INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE {VisitRange}
                GROUP BY d.Name
                ORDER BY COUNT(1) DESC, d.Name";
            return (await conn.QueryAsync<DashboardNameCountRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Top = top })).ToList();
        }

        public async Task<List<DashboardNameCountRow>> GetTopClinicDepartmentsAsync(DateTime fromUtc, DateTime toUtc, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = $@"
                SELECT TOP (@Top)
                       CASE WHEN v.DepartmentSnapshot = '' THEN '(tanpa department)' ELSE v.DepartmentSnapshot END AS Nama,
                       COUNT(1) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}
                GROUP BY CASE WHEN v.DepartmentSnapshot = '' THEN '(tanpa department)' ELSE v.DepartmentSnapshot END
                ORDER BY COUNT(1) DESC";
            return (await conn.QueryAsync<DashboardNameCountRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Top = top })).ToList();
        }

        private const string Deadline = GymRepository.DeadlineSql;

        public async Task<GymDashboardSummary> GetGymSummaryAsync(DateTime fromUtc, DateTime toUtc)
        {
            using var conn = _connectionFactory.CreateConnection();
            // Partisipasi dihitung terhadap karyawan AKTIF saja (karyawan nonaktif yang pernah beraktivitas tidak dihitung)
            var sql = $@"
                SELECT COUNT(1) AS TotalKunjungan,
                       COUNT(DISTINCT CASE WHEN e.IsActive = 1 THEN g.EmployeeId END) AS KaryawanUnik,
                       (SELECT COUNT(1) FROM dbo.Employees WHERE IsActive = 1 AND MitraId IS NULL) AS TotalKaryawanAktif,
                       AVG(CASE WHEN g.CheckOutStatus = 'Manual'
                                THEN CAST(DATEDIFF(MINUTE, g.CreatedAt, g.CheckOutAt) AS DECIMAL(10,1)) END) AS RataDurasiMenit,
                       ISNULL(SUM(CASE WHEN g.CheckOutStatus = 'Auto' OR (g.CheckOutStatus IS NULL AND {Deadline} <= @NowUtc)
                                       THEN 1 ELSE 0 END), 0) AS TidakCheckout,
                       ISNULL(SUM(CASE WHEN g.CheckOutStatus = 'Manual' THEN CAST(g.JarakTempuhMeter AS BIGINT) END), 0) AS TotalJarakMeter,
                       (SELECT COUNT(1) FROM dbo.GymAttendances g
                        WHERE g.CheckOutStatus IS NULL AND {Deadline} > @NowUtc) AS SedangDiGym
                FROM dbo.GymAttendances g
                INNER JOIN dbo.Employees e ON e.Id = g.EmployeeId
                WHERE g.CreatedAt >= @FromUtc AND g.CreatedAt < @ToUtc";
            return await conn.QuerySingleAsync<GymDashboardSummary>(sql, new
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                NowUtc = DateTime.UtcNow
            });
        }

        public async Task<List<DashboardBucketRow>> GetGymPerHourAsync(DateTime fromUtc, DateTime toUtc, int offset)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT DATEPART(HOUR, DATEADD(MINUTE, @Offset, g.CreatedAt)) AS Bucket,
                       COUNT(1) AS Jumlah, COUNT(DISTINCT g.EmployeeId) AS Karyawan
                FROM dbo.GymAttendances g
                WHERE g.CreatedAt >= @FromUtc AND g.CreatedAt < @ToUtc
                GROUP BY DATEPART(HOUR, DATEADD(MINUTE, @Offset, g.CreatedAt))";
            return (await conn.QueryAsync<DashboardBucketRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Offset = offset })).ToList();
        }

        public async Task<List<GymTopRow>> GetGymTopAsync(DateTime fromUtc, DateTime toUtc, int top)
        {
            using var conn = _connectionFactory.CreateConnection();
            // Berat terakhir = berat terakhir yang DIISI (aktivitas selain gym tidak mengisi berat)
            const string sql = @"
                WITH x AS (
                    SELECT g.EmployeeId, g.NamaSnapshot, g.DepartmentSnapshot, g.CreatedAt,
                           COUNT(1) OVER (PARTITION BY g.EmployeeId) AS Jumlah,
                           SUM(CASE WHEN g.CheckOutStatus = 'Manual' THEN DATEDIFF(MINUTE, g.CreatedAt, g.CheckOutAt) END)
                               OVER (PARTITION BY g.EmployeeId) AS TotalMenit,
                           SUM(CASE WHEN g.CheckOutStatus = 'Manual' THEN CAST(g.JarakTempuhMeter AS BIGINT) END)
                               OVER (PARTITION BY g.EmployeeId) AS TotalJarakMeter,
                           ROW_NUMBER() OVER (PARTITION BY g.EmployeeId ORDER BY g.CreatedAt DESC) AS rn
                    FROM dbo.GymAttendances g
                    WHERE g.CreatedAt >= @FromUtc AND g.CreatedAt < @ToUtc
                )
                SELECT TOP (@Top) x.EmployeeId, e.NIP, x.NamaSnapshot AS Nama, x.DepartmentSnapshot AS Department,
                       x.Jumlah, x.TotalMenit, x.TotalJarakMeter, x.CreatedAt AS TerakhirUtc,
                       (SELECT TOP 1 b.BeratBadan FROM dbo.GymAttendances b
                        WHERE b.EmployeeId = x.EmployeeId AND b.BeratBadan IS NOT NULL
                          AND b.CreatedAt >= @FromUtc AND b.CreatedAt < @ToUtc
                        ORDER BY b.CreatedAt DESC) AS BeratTerakhir
                FROM x
                INNER JOIN dbo.Employees e ON e.Id = x.EmployeeId
                WHERE x.rn = 1
                ORDER BY x.Jumlah DESC, x.TotalMenit DESC, x.CreatedAt DESC";
            return (await conn.QueryAsync<GymTopRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc, Top = top })).ToList();
        }

        public async Task<List<GymActivityRow>> GetGymPerActivityAsync(DateTime fromUtc, DateTime toUtc)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT g.ActivityTypeId,
                       MAX(ISNULL(g.AktivitasNama, N'Gym')) AS Nama,
                       MAX(ISNULL(g.AktivitasIkon, N'🏋️')) AS Ikon,
                       COUNT(1) AS Jumlah,
                       COUNT(DISTINCT g.EmployeeId) AS Karyawan,
                       AVG(CASE WHEN g.CheckOutStatus = 'Manual'
                                THEN CAST(DATEDIFF(MINUTE, g.CreatedAt, g.CheckOutAt) AS DECIMAL(10,1)) END) AS RataDurasiMenit,
                       ISNULL(SUM(CASE WHEN g.CheckOutStatus = 'Manual' THEN CAST(g.JarakTempuhMeter AS BIGINT) END), 0) AS TotalJarakMeter
                FROM dbo.GymAttendances g
                WHERE g.CreatedAt >= @FromUtc AND g.CreatedAt < @ToUtc
                GROUP BY g.ActivityTypeId
                ORDER BY COUNT(1) DESC";
            return (await conn.QueryAsync<GymActivityRow>(sql, new { FromUtc = fromUtc, ToUtc = toUtc })).ToList();
        }
    }
}
