using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public class DrugTestQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string? Department { get; set; }
        public string? Hasil { get; set; }        // Positif / Negatif / null = semua
        public string? Jenis { get; set; }        // urin / alkohol / null = semua
        public string? Search { get; set; }
        public string? Kelompok { get; set; }     // akp / mitra / null = semua
    }

    public interface IDrugAlcoholTestRepository
    {
        Task<int> CreateAsync(DrugAlcoholTest test, IEnumerable<DrugTestUrineItem> urineItems);
        Task<DrugAlcoholTest?> GetByIdAsync(int id);
        Task DeleteAsync(int id);
        Task<List<DrugAlcoholTest>> GetListAsync(DrugTestQuery q, int top, bool withItems = false);
        Task<List<DrugAlcoholTest>> GetByEmployeeAsync(int employeeId, int top = 100);
    }

    /// <summary>Tes Urin (parameter Negatif/Positif) & Tes Alkohol (kadar dari alat). Karyawan AKP & mitra.</summary>
    public class DrugAlcoholTestRepository : IDrugAlcoholTestRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DrugAlcoholTestRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private const string SelectSql = @"
            SELECT t.*, e.NIP, e.MitraId,
                   (SELECT COUNT(1) FROM dbo.MedicalAttachments a WHERE a.EntityType = 'DrugTest' AND a.EntityId = t.Id) AS AttachmentCount
            FROM dbo.DrugAlcoholTests t
            INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId";

        public async Task<int> CreateAsync(DrugAlcoholTest t, IEnumerable<DrugTestUrineItem> urineItems)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var id = await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.DrugAlcoholTests
                        (EmployeeId, NamaSnapshot, DepartmentSnapshot, SectionSnapshot, PositionSnapshot, TestedAt, Alasan,
                         TesUrin, UrinHasil, TesAlkohol, AlkoholKadar, AlkoholBatas, AlkoholHasil, Catatan,
                         ExaminerAdminId, ExaminerName, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES
                        (@EmployeeId, @NamaSnapshot, @DepartmentSnapshot, @SectionSnapshot, @PositionSnapshot, @TestedAt, @Alasan,
                         @TesUrin, @UrinHasil, @TesAlkohol, @AlkoholKadar, @AlkoholBatas, @AlkoholHasil, @Catatan,
                         @ExaminerAdminId, @ExaminerName, SYSUTCDATETIME())", t, tx);

                foreach (var item in urineItems)
                    await conn.ExecuteAsync(
                        "INSERT INTO dbo.DrugAlcoholTestUrineItems (TestId, ParameterName, Hasil) VALUES (@TestId, @ParameterName, @Hasil)",
                        new { TestId = id, item.ParameterName, item.Hasil }, tx);

                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<DrugAlcoholTest?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync($@"
                {SelectSql} WHERE t.Id = @Id;
                SELECT * FROM dbo.DrugAlcoholTestUrineItems WHERE TestId = @Id ORDER BY Id;", new { Id = id });
            var test = await multi.ReadFirstOrDefaultAsync<DrugAlcoholTest>();
            if (test is null)
                return null;
            test.UrineItems = (await multi.ReadAsync<DrugTestUrineItem>()).ToList();
            return test;
        }

        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            // Hasil parameter urin ikut terhapus (ON DELETE CASCADE)
            await conn.ExecuteAsync("DELETE FROM dbo.DrugAlcoholTests WHERE Id = @Id", new { Id = id });
        }

        public async Task<List<DrugAlcoholTest>> GetListAsync(DrugTestQuery q, int top, bool withItems = false)
        {
            var like = MedicalSql.Like(q.Search);
            var sql = $@"
                SELECT TOP (@Top) * FROM ({SelectSql}
                    WHERE (@FromUtc IS NULL OR t.TestedAt >= @FromUtc)
                      AND (@ToUtc   IS NULL OR t.TestedAt <  @ToUtc)
                      AND (@Department IS NULL OR t.DepartmentSnapshot = @Department)
                      AND (@Kelompok IS NULL OR (@Kelompok = 'akp' AND e.MitraId IS NULL) OR (@Kelompok = 'mitra' AND e.MitraId IS NOT NULL))
                      AND (@Jenis IS NULL OR (@Jenis = 'urin' AND t.TesUrin = 1) OR (@Jenis = 'alkohol' AND t.TesAlkohol = 1))
                      AND (@Hasil IS NULL
                           OR (@Hasil = 'Positif' AND (t.UrinHasil = 'Positif' OR t.AlkoholHasil = 'Positif'))
                           OR (@Hasil = 'Negatif' AND ISNULL(t.UrinHasil, 'Negatif') <> 'Positif' AND ISNULL(t.AlkoholHasil, 'Negatif') <> 'Positif'))
                      AND (@Like IS NULL OR e.NIP LIKE @Like OR t.NamaSnapshot LIKE @Like OR t.DepartmentSnapshot LIKE @Like)
                ) x
                ORDER BY x.TestedAt DESC, x.Id DESC";

            using var conn = _connectionFactory.CreateConnection();
            var tests = (await conn.QueryAsync<DrugAlcoholTest>(sql, new
            {
                Top = top,
                q.FromUtc,
                q.ToUtc,
                Department = string.IsNullOrWhiteSpace(q.Department) ? null : q.Department,
                Jenis = q.Jenis is "urin" or "alkohol" ? q.Jenis : null,
                Kelompok = q.Kelompok is "akp" or "mitra" ? q.Kelompok : null,
                Hasil = q.Hasil is TestResults.Positif or TestResults.Negatif ? q.Hasil : null,
                Like = like
            })).ToList();

            if (withItems)
                await LoadItemsAsync(conn, tests);
            return tests;
        }

        public async Task<List<DrugAlcoholTest>> GetByEmployeeAsync(int employeeId, int top = 100)
        {
            using var conn = _connectionFactory.CreateConnection();
            var tests = (await conn.QueryAsync<DrugAlcoholTest>($@"
                SELECT TOP (@Top) * FROM ({SelectSql} WHERE t.EmployeeId = @EmployeeId) x
                ORDER BY x.TestedAt DESC, x.Id DESC", new { EmployeeId = employeeId, Top = top })).ToList();
            await LoadItemsAsync(conn, tests);
            return tests;
        }

        private static async Task LoadItemsAsync(System.Data.IDbConnection conn, List<DrugAlcoholTest> tests)
        {
            if (tests.Count == 0)
                return;
            var items = new List<DrugTestUrineItem>();
            foreach (var chunk in tests.Select(t => t.Id).Chunk(1000))
                items.AddRange(await conn.QueryAsync<DrugTestUrineItem>(
                    "SELECT * FROM dbo.DrugAlcoholTestUrineItems WHERE TestId IN @Ids ORDER BY Id", new { Ids = chunk }));
            var byTest = items.GroupBy(i => i.TestId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var t in tests)
                t.UrineItems = byTest.TryGetValue(t.Id, out var list) ? list : new List<DrugTestUrineItem>();
        }
    }
}
