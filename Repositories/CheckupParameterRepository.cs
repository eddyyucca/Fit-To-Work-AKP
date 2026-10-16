using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Parameter pemkes, kriteria/poin, dan interpretasi total skor (menu Parameter &amp; Skor Pemkes).</summary>
    public interface ICheckupParameterRepository
    {
        Task<List<CheckupParameter>> GetAllAsync(bool onlyActive);
        Task<CheckupParameter?> GetByIdAsync(int id);
        Task<bool> NameExistsAsync(string name, int? excludeId);

        /// <summary>Simpan parameter + seluruh kriterianya (kriteria lama diganti). Mengembalikan Id.</summary>
        Task<int> SaveAsync(CheckupParameter parameter, IEnumerable<CheckupCriterion> criteria);

        Task SetActiveAsync(int id, bool active);

        /// <summary>Hapus permanen; false kalau parameter sudah pernah dipakai di pemeriksaan.</summary>
        Task<bool> DeleteAsync(int id);

        Task<List<CheckupRiskLevel>> GetRiskLevelsAsync();
        Task SaveRiskLevelsAsync(IEnumerable<CheckupRiskLevel> levels);
    }

    public class CheckupParameterRepository : ICheckupParameterRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public CheckupParameterRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private const string SelectSql = @"
            SELECT p.*, (SELECT COUNT(1) FROM dbo.HealthCheckupValues v WHERE v.ParameterId = p.Id) AS UsageCount
            FROM dbo.CheckupParameters p";

        public async Task<List<CheckupParameter>> GetAllAsync(bool onlyActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync($@"
                {SelectSql} WHERE (@OnlyActive = 0 OR p.IsActive = 1) ORDER BY p.SortOrder, p.Id;
                SELECT c.* FROM dbo.CheckupParameterCriteria c
                INNER JOIN dbo.CheckupParameters p ON p.Id = c.ParameterId
                WHERE (@OnlyActive = 0 OR p.IsActive = 1)
                ORDER BY c.ParameterId, c.SortOrder, c.Id;", new { OnlyActive = onlyActive });

            var parameters = (await multi.ReadAsync<CheckupParameter>()).ToList();
            var criteria = (await multi.ReadAsync<CheckupCriterion>()).GroupBy(c => c.ParameterId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var p in parameters)
                p.Criteria = criteria.TryGetValue(p.Id, out var list) ? list : new List<CheckupCriterion>();
            return parameters;
        }

        public async Task<CheckupParameter?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync($@"
                {SelectSql} WHERE p.Id = @Id;
                SELECT * FROM dbo.CheckupParameterCriteria WHERE ParameterId = @Id ORDER BY SortOrder, Id;", new { Id = id });
            var p = await multi.ReadFirstOrDefaultAsync<CheckupParameter>();
            if (p is not null)
                p.Criteria = (await multi.ReadAsync<CheckupCriterion>()).ToList();
            return p;
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.CheckupParameters WHERE Name = @Name AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
                new { Name = name, ExcludeId = excludeId }) > 0;
        }

        public async Task<int> SaveAsync(CheckupParameter p, IEnumerable<CheckupCriterion> criteria)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                int id;
                if (p.Id <= 0)
                {
                    id = await conn.ExecuteScalarAsync<int>(@"
                        INSERT INTO dbo.CheckupParameters (Name, Unit, InputType, Decimals, MinInput, MaxInput, IsScored, Hint, SortOrder, IsActive, CreatedAt)
                        OUTPUT INSERTED.Id
                        VALUES (@Name, @Unit, @InputType, @Decimals, @MinInput, @MaxInput, @IsScored, @Hint, @SortOrder, @IsActive, SYSUTCDATETIME())", p, tx);
                }
                else
                {
                    id = p.Id;
                    await conn.ExecuteAsync(@"
                        UPDATE dbo.CheckupParameters
                        SET Name = @Name, Unit = @Unit, InputType = @InputType, Decimals = @Decimals, MinInput = @MinInput, MaxInput = @MaxInput,
                            IsScored = @IsScored, Hint = @Hint, SortOrder = @SortOrder, IsActive = @IsActive
                        WHERE Id = @Id", p, tx);
                    // Riwayat pemeriksaan menyimpan snapshot kriteria & poin, jadi kriteria boleh diganti total
                    await conn.ExecuteAsync("DELETE FROM dbo.CheckupParameterCriteria WHERE ParameterId = @Id", new { Id = id }, tx);
                }

                var order = 10;
                foreach (var c in criteria)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.CheckupParameterCriteria (ParameterId, Label, MinValue, MaxValue, MatchText, ForSex, Points, Level, SortOrder)
                        VALUES (@ParameterId, @Label, @MinValue, @MaxValue, @MatchText, @ForSex, @Points, @Level, @SortOrder)",
                        new
                        {
                            ParameterId = id, c.Label, c.MinValue, c.MaxValue, c.MatchText, c.ForSex, c.Points,
                            Level = c.Level ?? CheckupLevels.None, SortOrder = order
                        }, tx);
                    order += 10;
                }

                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.CheckupParameters SET IsActive = @Active WHERE Id = @Id", new { Id = id, Active = active });
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            // Kriteria ikut terhapus (ON DELETE CASCADE)
            return await conn.ExecuteAsync(@"
                DELETE p FROM dbo.CheckupParameters p
                WHERE p.Id = @Id AND NOT EXISTS (SELECT 1 FROM dbo.HealthCheckupValues v WHERE v.ParameterId = p.Id)", new { Id = id }) > 0;
        }

        public async Task<List<CheckupRiskLevel>> GetRiskLevelsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<CheckupRiskLevel>(
                "SELECT * FROM dbo.CheckupRiskLevels ORDER BY SortOrder, MinScore")).ToList();
        }

        public async Task SaveRiskLevelsAsync(IEnumerable<CheckupRiskLevel> levels)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync("DELETE FROM dbo.CheckupRiskLevels", transaction: tx);
                var order = 10;
                foreach (var l in levels.OrderBy(l => l.MinScore))
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.CheckupRiskLevels (MinScore, MaxScore, Label, Color, Description, SortOrder)
                        VALUES (@MinScore, @MaxScore, @Label, @Color, @Description, @SortOrder)",
                        new { l.MinScore, l.MaxScore, l.Label, l.Color, l.Description, SortOrder = order }, tx);
                    order += 10;
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
