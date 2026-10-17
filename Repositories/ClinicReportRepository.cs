using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Services;

namespace FitToWorkApp.Repositories
{
    /// <summary>Rentang laporan: batas UTC, filter department (null = semua) dan selisih zona waktu site (menit).</summary>
    public record ReportFilter(DateTime FromUtc, DateTime ToUtc, string? Department, int OffsetMinutes);

    /// <summary>
    /// Query laporan klinik. HANYA MEMBACA (SELECT): tidak ada INSERT / UPDATE / DELETE dan tidak mengubah skema.
    /// Kunjungan yang dibatalkan (IsCancelled = 1) tidak dihitung, sama seperti dashboard.
    /// </summary>
    public interface IClinicReportRepository
    {
        // Kunjungan & obat
        Task<ReportSummary> GetSummaryAsync(ReportFilter f);
        Task<List<ReportDailyRow>> GetDailyTrendAsync(ReportFilter f, DateTime fromLocalDate);
        Task<List<ReportDeptRow>> GetByDepartmentAsync(ReportFilter f);
        Task<List<ReportNameCountRow>> GetByCategoryAsync(ReportFilter f);
        Task<List<ReportDiseaseRow>> GetTopDiseasesAsync(ReportFilter f, int top);
        Task<List<ReportDiseaseMonthRow>> GetDiseaseMonthlyAsync(ReportFilter f, int topDiseases);
        Task<List<ReportRepeatVisitorRow>> GetRepeatVisitorsAsync(ReportFilter f, int minVisits, int top);
        Task<List<ReportNameCountRow>> GetRepeatDiseasesAsync(ReportFilter f, int withinDays);
        Task<List<ReportProfileRow>> GetProfileAsync(ReportFilter f);
        Task<List<ReportHeatRow>> GetHeatmapAsync(ReportFilter f);
        Task<List<ReportMedicineUseRow>> GetTopMedicinesAsync(ReportFilter f, int top);
        Task<List<ReportStockRow>> GetStockAsync();

        // Pemeriksaan kesehatan
        Task<ReportCheckupSummary> GetCheckupSummaryAsync(ReportFilter f, DateTime todayLocal);
        Task<List<ReportLabelCountRow>> GetRiskDistributionAsync(ReportFilter f);
        Task<List<ReportCheckupDeptRow>> GetCheckupByDepartmentAsync(ReportFilter f);
        Task<List<ReportFollowUpRow>> GetOverdueFollowUpsAsync(ReportFilter f, DateTime todayLocal, int top);

        // Tes urin & alkohol
        Task<ReportDrugSummary> GetDrugSummaryAsync(ReportFilter f);
        Task<List<ReportDrugGroupRow>> GetDrugByReasonAsync(ReportFilter f);
        Task<List<ReportDrugGroupRow>> GetDrugByDepartmentAsync(ReportFilter f);
    }

    public class ClinicReportRepository : IClinicReportRepository
    {
        private const string VisitRange =
            "v.IsCancelled = 0 AND v.VisitAt >= @FromUtc AND v.VisitAt < @ToUtc AND (@Dept IS NULL OR v.DepartmentSnapshot = @Dept)";
        private const string CheckupRange =
            "c.ExaminedAt >= @FromUtc AND c.ExaminedAt < @ToUtc AND (@Dept IS NULL OR c.DepartmentSnapshot = @Dept)";
        private const string DrugRange =
            "t.TestedAt >= @FromUtc AND t.TestedAt < @ToUtc AND (@Dept IS NULL OR t.DepartmentSnapshot = @Dept)";

        private const string DeptLabelVisit = "CASE WHEN v.DepartmentSnapshot = '' THEN '(tanpa department)' ELSE v.DepartmentSnapshot END";
        private const string LocalVisitAt = "DATEADD(MINUTE, @Offset, v.VisitAt)";

        private readonly IDbConnectionFactory _connectionFactory;

        public ClinicReportRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private static object P(ReportFilter f) => new
        {
            f.FromUtc,
            f.ToUtc,
            Dept = f.Department,
            Offset = f.OffsetMinutes
        };

        private async Task<List<T>> ListAsync<T>(string sql, object param)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<T>(sql, param)).ToList();
        }

        // ============================ Kunjungan & obat ============================

        public async Task<ReportSummary> GetSummaryAsync(ReportFilter f)
        {
            const string sql = $@"
                SELECT COUNT(1) AS TotalKunjungan,
                       COUNT(DISTINCT v.EmployeeId) AS KaryawanUnik,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'KembaliBekerja' THEN 1 ELSE 0 END), 0) AS KembaliBekerja,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'KerjaTerbatas' THEN 1 ELSE 0 END), 0) AS KerjaTerbatas,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN 1 ELSE 0 END), 0) AS Istirahat,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Rujuk' THEN 1 ELSE 0 END), 0) AS Rujuk,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN ISNULL(v.HariIstirahat, 0) ELSE 0 END), 0) AS TotalHariIstirahat,
                       (SELECT ISNULL(SUM(m.Qty), 0)
                        FROM dbo.ClinicVisitMedicines m
                        INNER JOIN dbo.ClinicVisits v2 ON v2.Id = m.ClinicVisitId
                        WHERE v2.IsCancelled = 0 AND v2.VisitAt >= @FromUtc AND v2.VisitAt < @ToUtc
                          AND (@Dept IS NULL OR v2.DepartmentSnapshot = @Dept)) AS ObatKeluar
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}";
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QuerySingleAsync<ReportSummary>(sql, P(f));
        }

        public Task<List<ReportDailyRow>> GetDailyTrendAsync(ReportFilter f, DateTime fromLocalDate)
        {
            const string bucket = $"DATEDIFF(DAY, @FromLocal, CAST({LocalVisitAt} AS DATE))";
            const string sql = $@"
                SELECT {bucket} AS Bucket, COUNT(1) AS Jumlah
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}
                GROUP BY {bucket}";
            return ListAsync<ReportDailyRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, FromLocal = fromLocalDate.Date });
        }

        public async Task<List<ReportDeptRow>> GetByDepartmentAsync(ReportFilter f)
        {
            const string sql = $@"
                SELECT {DeptLabelVisit} AS Department,
                       COUNT(1) AS Jumlah,
                       COUNT(DISTINCT v.EmployeeId) AS Karyawan,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN ISNULL(v.HariIstirahat, 0) ELSE 0 END), 0) AS HariIstirahat,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Rujuk' THEN 1 ELSE 0 END), 0) AS Rujuk
                FROM dbo.ClinicVisits v
                WHERE {VisitRange}
                GROUP BY {DeptLabelVisit}
                ORDER BY COUNT(1) DESC";
            var rows = await ListAsync<ReportDeptRow>(sql, P(f));

            // Jumlah karyawan AKP aktif per department, untuk menghitung kasus per 100 karyawan
            var active = await ListAsync<ReportNameCountRow>(@"
                SELECT d.Name AS Nama, COUNT(1) AS Jumlah
                FROM dbo.Employees e
                INNER JOIN dbo.Departments d ON d.Id = e.DepartmentId
                WHERE e.IsActive = 1 AND e.MitraId IS NULL
                GROUP BY d.Name", new { });
            var byName = active.ToDictionary(a => EmployeeImportRules.Key(a.Nama), a => a.Jumlah);
            foreach (var r in rows)
                if (byName.TryGetValue(EmployeeImportRules.Key(r.Department), out var n))
                    r.KaryawanAktif = n;
            return rows;
        }

        public Task<List<ReportNameCountRow>> GetByCategoryAsync(ReportFilter f)
        {
            const string cat = "ISNULL(NULLIF(LTRIM(RTRIM(d.Kategori)), ''), '(tanpa kategori)')";
            const string sql = $@"
                SELECT {cat} AS Nama, COUNT(1) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisitDiseases vd
                INNER JOIN dbo.ClinicVisits v ON v.Id = vd.ClinicVisitId
                INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE {VisitRange}
                GROUP BY {cat}
                ORDER BY COUNT(1) DESC";
            return ListAsync<ReportNameCountRow>(sql, P(f));
        }

        public Task<List<ReportDiseaseRow>> GetTopDiseasesAsync(ReportFilter f, int top)
        {
            const string sql = $@"
                SELECT TOP (@Top) d.Kode, d.Name AS Nama, d.Kategori, COUNT(1) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisitDiseases vd
                INNER JOIN dbo.ClinicVisits v ON v.Id = vd.ClinicVisitId
                INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE {VisitRange}
                GROUP BY d.Id, d.Kode, d.Name, d.Kategori
                ORDER BY COUNT(1) DESC, d.Name";
            return ListAsync<ReportDiseaseRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Top = top });
        }

        public Task<List<ReportDiseaseMonthRow>> GetDiseaseMonthlyAsync(ReportFilter f, int topDiseases)
        {
            const string month = $"DATEFROMPARTS(YEAR({LocalVisitAt}), MONTH({LocalVisitAt}), 1)";
            const string sql = $@"
                ;WITH Top5 AS (
                    SELECT TOP (@Top) vd.DiseaseId
                    FROM dbo.ClinicVisitDiseases vd
                    INNER JOIN dbo.ClinicVisits v ON v.Id = vd.ClinicVisitId
                    WHERE {VisitRange}
                    GROUP BY vd.DiseaseId
                    ORDER BY COUNT(1) DESC
                )
                SELECT {month} AS Bulan, d.Name AS Nama, COUNT(1) AS Jumlah
                FROM dbo.ClinicVisitDiseases vd
                INNER JOIN dbo.ClinicVisits v ON v.Id = vd.ClinicVisitId
                INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE {VisitRange} AND vd.DiseaseId IN (SELECT DiseaseId FROM Top5)
                GROUP BY {month}, d.Name
                ORDER BY Bulan";
            return ListAsync<ReportDiseaseMonthRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Top = topDiseases });
        }

        public Task<List<ReportRepeatVisitorRow>> GetRepeatVisitorsAsync(ReportFilter f, int minVisits, int top)
        {
            const string sql = $@"
                SELECT TOP (@Top) e.NIP, MAX(v.NamaSnapshot) AS Nama, MAX(v.DepartmentSnapshot) AS Department,
                       COUNT(1) AS Jumlah,
                       ISNULL(SUM(CASE WHEN v.Rekomendasi = 'Istirahat' THEN ISNULL(v.HariIstirahat, 0) ELSE 0 END), 0) AS HariIstirahat,
                       MAX(v.VisitAt) AS TerakhirUtc
                FROM dbo.ClinicVisits v
                INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId
                WHERE {VisitRange}
                GROUP BY e.Id, e.NIP
                HAVING COUNT(1) >= @Min
                ORDER BY COUNT(1) DESC, MAX(v.VisitAt) DESC";
            return ListAsync<ReportRepeatVisitorRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Top = top, Min = minVisits });
        }

        public Task<List<ReportNameCountRow>> GetRepeatDiseasesAsync(ReportFilter f, int withinDays)
        {
            // Kasus berulang: karyawan yang sama didiagnosa penyakit yang sama lagi dalam N hari sejak kunjungan sebelumnya
            const string sql = $@"
                SELECT d.Name AS Nama, COUNT(DISTINCT v.Id) AS Jumlah, COUNT(DISTINCT v.EmployeeId) AS Karyawan
                FROM dbo.ClinicVisits v
                INNER JOIN dbo.ClinicVisitDiseases vd ON vd.ClinicVisitId = v.Id
                INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE {VisitRange}
                  AND EXISTS (
                        SELECT 1
                        FROM dbo.ClinicVisits v1
                        INNER JOIN dbo.ClinicVisitDiseases vd1 ON vd1.ClinicVisitId = v1.Id
                        WHERE v1.IsCancelled = 0 AND v1.EmployeeId = v.EmployeeId AND vd1.DiseaseId = vd.DiseaseId
                          AND v1.Id <> v.Id AND v1.VisitAt < v.VisitAt AND v1.VisitAt >= DATEADD(DAY, -@Days, v.VisitAt))
                GROUP BY d.Name
                ORDER BY COUNT(DISTINCT v.Id) DESC, d.Name";
            return ListAsync<ReportNameCountRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Days = withinDays });
        }

        public Task<List<ReportProfileRow>> GetProfileAsync(ReportFilter f)
        {
            const string sql = $@"
                SELECT JenisKelamin, KelompokUmur, COUNT(1) AS Jumlah
                FROM (
                    SELECT ISNULL(e.JenisKelamin, '') AS JenisKelamin,
                           CASE WHEN e.TanggalLahir IS NULL THEN 'Tidak diketahui'
                                WHEN a.Age < 25 THEN '< 25'
                                WHEN a.Age < 35 THEN '25-34'
                                WHEN a.Age < 45 THEN '35-44'
                                WHEN a.Age < 55 THEN '45-54'
                                ELSE '55+' END AS KelompokUmur
                    FROM dbo.ClinicVisits v
                    INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId
                    CROSS APPLY (SELECT DATEDIFF(YEAR, e.TanggalLahir, v.VisitAt)
                                        - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, e.TanggalLahir, v.VisitAt), e.TanggalLahir) > CAST(v.VisitAt AS DATE) THEN 1 ELSE 0 END AS Age) a
                    WHERE {VisitRange}
                ) x
                GROUP BY JenisKelamin, KelompokUmur";
            return ListAsync<ReportProfileRow>(sql, P(f));
        }

        public Task<List<ReportHeatRow>> GetHeatmapAsync(ReportFilter f)
        {
            // 1900-01-01 adalah hari Senin → sisa bagi 7 = indeks hari (0 = Senin), tidak bergantung DATEFIRST server
            const string sql = $@"
                SELECT Hari, Jam, COUNT(1) AS Jumlah
                FROM (
                    SELECT DATEDIFF(DAY, '19000101', CAST({LocalVisitAt} AS DATE)) % 7 AS Hari,
                           DATEPART(HOUR, {LocalVisitAt}) AS Jam
                    FROM dbo.ClinicVisits v
                    WHERE {VisitRange}
                ) x
                GROUP BY Hari, Jam";
            return ListAsync<ReportHeatRow>(sql, P(f));
        }

        public Task<List<ReportMedicineUseRow>> GetTopMedicinesAsync(ReportFilter f, int top)
        {
            const string sql = $@"
                SELECT TOP (@Top) MAX(m.NamaSnapshot) AS Nama, MAX(m.SatuanSnapshot) AS Satuan,
                       SUM(m.Qty) AS Qty, COUNT(DISTINCT m.ClinicVisitId) AS Kunjungan
                FROM dbo.ClinicVisitMedicines m
                INNER JOIN dbo.ClinicVisits v ON v.Id = m.ClinicVisitId
                WHERE {VisitRange}
                GROUP BY m.MedicineId
                ORDER BY SUM(m.Qty) DESC";
            return ListAsync<ReportMedicineUseRow>(sql, new { f.FromUtc, f.ToUtc, Dept = f.Department, Offset = f.OffsetMinutes, Top = top });
        }

        public Task<List<ReportStockRow>> GetStockAsync()
        {
            // Stok sekarang + pemakaian 30 hari terakhir (tidak bergantung rentang laporan)
            const string sql = @"
                SELECT m.Id, m.Name AS Nama, m.Satuan, m.Stok, m.StokMinimum, ISNULL(u.Qty30, 0) AS Qty30Hari
                FROM dbo.Medicines m
                LEFT JOIN (
                    SELECT cm.MedicineId, SUM(cm.Qty) AS Qty30
                    FROM dbo.ClinicVisitMedicines cm
                    INNER JOIN dbo.ClinicVisits v ON v.Id = cm.ClinicVisitId
                    WHERE v.IsCancelled = 0 AND v.VisitAt >= DATEADD(DAY, -30, SYSUTCDATETIME())
                    GROUP BY cm.MedicineId
                ) u ON u.MedicineId = m.Id
                WHERE m.IsActive = 1
                ORDER BY m.Name";
            return ListAsync<ReportStockRow>(sql, new { });
        }

        // ============================ Pemeriksaan kesehatan ============================

        private class FollowUpCounts
        {
            public int Pending { get; set; }
            public int Overdue { get; set; }
        }

        public async Task<ReportCheckupSummary> GetCheckupSummaryAsync(ReportFilter f, DateTime todayLocal)
        {
            using var conn = _connectionFactory.CreateConnection();
            var p = P(f);

            var summary = await conn.QuerySingleAsync<ReportCheckupSummary>($@"
                SELECT COUNT(1) AS Total, COUNT(DISTINCT c.EmployeeId) AS Karyawan,
                       ISNULL(SUM(CASE WHEN c.AdaTemuan = 1 THEN 1 ELSE 0 END), 0) AS AdaTemuan
                FROM dbo.HealthCheckups c
                WHERE {CheckupRange}", p);

            summary.KaryawanAktif = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM dbo.Employees e
                LEFT JOIN dbo.Departments d ON d.Id = e.DepartmentId
                WHERE e.IsActive = 1 AND e.MitraId IS NULL AND (@Dept IS NULL OR d.Name = @Dept)", new { Dept = f.Department });

            var fu = await conn.QuerySingleAsync<FollowUpCounts>(@"
                SELECT ISNULL(SUM(CASE WHEN c.FollowUpDoneAt IS NULL THEN 1 ELSE 0 END), 0) AS Pending,
                       ISNULL(SUM(CASE WHEN c.FollowUpDoneAt IS NULL AND c.FollowUpDate < @TodayLocal THEN 1 ELSE 0 END), 0) AS Overdue
                FROM dbo.HealthCheckups c
                WHERE c.FollowUpDate IS NOT NULL AND (@Dept IS NULL OR c.DepartmentSnapshot = @Dept)",
                new { Dept = f.Department, TodayLocal = todayLocal.Date });
            summary.FollowUpBelumSelesai = fu.Pending;
            summary.FollowUpTerlambat = fu.Overdue;
            return summary;
        }

        public Task<List<ReportLabelCountRow>> GetRiskDistributionAsync(ReportFilter f)
        {
            const string label = "ISNULL(NULLIF(c.RisikoLabel, ''), 'Belum dinilai')";
            const string sql = $@"
                SELECT {label} AS Label, COUNT(1) AS Jumlah
                FROM dbo.HealthCheckups c
                WHERE {CheckupRange}
                GROUP BY {label}
                ORDER BY COUNT(1) DESC";
            return ListAsync<ReportLabelCountRow>(sql, P(f));
        }

        public Task<List<ReportCheckupDeptRow>> GetCheckupByDepartmentAsync(ReportFilter f)
        {
            // Batas "tinggi" mengikuti acuan nilai pemkes yang sedang dipakai aplikasi (menu Acuan Nilai Pemkes)
            const string dept = "CASE WHEN c.DepartmentSnapshot = '' THEN '(tanpa department)' ELSE c.DepartmentSnapshot END";
            const string sql = $@"
                SELECT {dept} AS Department, COUNT(1) AS Total,
                       CAST(AVG(CAST(c.Sistolik AS DECIMAL(9, 2))) AS DECIMAL(9, 1)) AS RataSistolik,
                       CAST(AVG(CAST(c.Diastolik AS DECIMAL(9, 2))) AS DECIMAL(9, 1)) AS RataDiastolik,
                       ISNULL(SUM(CASE WHEN c.Sistolik >= @TdSis OR c.Diastolik >= @TdDia THEN 1 ELSE 0 END), 0) AS TdTinggi,
                       ISNULL(SUM(CASE WHEN (c.GulaDarahJenis = 'Puasa' AND c.GulaDarah >= @GulaPuasa)
                                         OR (c.GulaDarahJenis <> 'Puasa' AND c.GulaDarah >= @GulaSewaktu) THEN 1 ELSE 0 END), 0) AS GulaTinggi,
                       ISNULL(SUM(CASE WHEN c.Kolesterol >= @Kolesterol THEN 1 ELSE 0 END), 0) AS KolesterolTinggi
                FROM dbo.HealthCheckups c
                WHERE {CheckupRange}
                GROUP BY {dept}
                ORDER BY COUNT(1) DESC";
            return ListAsync<ReportCheckupDeptRow>(sql, new
            {
                f.FromUtc,
                f.ToUtc,
                Dept = f.Department,
                TdSis = HealthRanges.Get(HealthReferenceKeys.TdTinggiSistolik),
                TdDia = HealthRanges.Get(HealthReferenceKeys.TdTinggiDiastolik),
                GulaPuasa = HealthRanges.Get(HealthReferenceKeys.GulaPuasaTinggi),
                GulaSewaktu = HealthRanges.Get(HealthReferenceKeys.GulaSewaktuTinggi),
                Kolesterol = HealthRanges.Get(HealthReferenceKeys.KolesterolTinggi)
            });
        }

        public Task<List<ReportFollowUpRow>> GetOverdueFollowUpsAsync(ReportFilter f, DateTime todayLocal, int top)
        {
            const string sql = @"
                SELECT TOP (@Top) e.NIP, c.NamaSnapshot AS Nama, c.DepartmentSnapshot AS Department,
                       c.ExaminedAt AS ExaminedAtUtc, c.FollowUpDate,
                       DATEDIFF(DAY, c.FollowUpDate, @TodayLocal) AS HariTerlambat
                FROM dbo.HealthCheckups c
                INNER JOIN dbo.Employees e ON e.Id = c.EmployeeId
                WHERE c.FollowUpDate IS NOT NULL AND c.FollowUpDoneAt IS NULL AND c.FollowUpDate < @TodayLocal
                  AND (@Dept IS NULL OR c.DepartmentSnapshot = @Dept)
                ORDER BY c.FollowUpDate";
            return ListAsync<ReportFollowUpRow>(sql, new { Dept = f.Department, TodayLocal = todayLocal.Date, Top = top });
        }

        // ============================ Tes urin & alkohol ============================

        public async Task<ReportDrugSummary> GetDrugSummaryAsync(ReportFilter f)
        {
            const string sql = $@"
                SELECT COUNT(1) AS Total,
                       ISNULL(SUM(CASE WHEN t.TesUrin = 1 THEN 1 ELSE 0 END), 0) AS TesUrin,
                       ISNULL(SUM(CASE WHEN t.TesUrin = 1 AND t.UrinHasil = 'Positif' THEN 1 ELSE 0 END), 0) AS UrinPositif,
                       ISNULL(SUM(CASE WHEN t.TesAlkohol = 1 THEN 1 ELSE 0 END), 0) AS TesAlkohol,
                       ISNULL(SUM(CASE WHEN t.TesAlkohol = 1 AND t.AlkoholHasil = 'Positif' THEN 1 ELSE 0 END), 0) AS AlkoholPositif
                FROM dbo.DrugAlcoholTests t
                WHERE {DrugRange}";
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QuerySingleAsync<ReportDrugSummary>(sql, P(f));
        }

        public Task<List<ReportDrugGroupRow>> GetDrugByReasonAsync(ReportFilter f) => DrugGroupAsync(f, "t.Alasan");

        public Task<List<ReportDrugGroupRow>> GetDrugByDepartmentAsync(ReportFilter f) =>
            DrugGroupAsync(f, "CASE WHEN t.DepartmentSnapshot = '' THEN '(tanpa department)' ELSE t.DepartmentSnapshot END");

        // groupExpr hanya dari konstanta di atas (bukan input pengguna)
        private Task<List<ReportDrugGroupRow>> DrugGroupAsync(ReportFilter f, string groupExpr)
        {
            var sql = $@"
                SELECT {groupExpr} AS Nama, COUNT(1) AS Total,
                       ISNULL(SUM(CASE WHEN t.TesUrin = 1 AND t.UrinHasil = 'Positif' THEN 1 ELSE 0 END), 0) AS UrinPositif,
                       ISNULL(SUM(CASE WHEN t.TesAlkohol = 1 AND t.AlkoholHasil = 'Positif' THEN 1 ELSE 0 END), 0) AS AlkoholPositif
                FROM dbo.DrugAlcoholTests t
                WHERE {DrugRange}
                GROUP BY {groupExpr}
                ORDER BY COUNT(1) DESC";
            return ListAsync<ReportDrugGroupRow>(sql, P(f));
        }
    }
}
