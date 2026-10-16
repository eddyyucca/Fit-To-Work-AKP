using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Services;

namespace FitToWorkApp.Repositories
{
    /// <summary>
    /// Acuan nilai pemkes yang diubah admin (dbo.HealthReferenceValues).
    /// Key yang tidak ada di tabel = memakai nilai default di <see cref="HealthRanges.Definitions"/>.
    /// </summary>
    public interface IHealthReferenceRepository
    {
        Task<Dictionary<string, decimal>> GetOverridesAsync();

        /// <summary>Simpan semua nilai lalu terapkan ke <see cref="HealthRanges"/>.</summary>
        Task SaveAsync(IReadOnlyDictionary<string, decimal> values, string updatedBy);

        /// <summary>Hapus semua perubahan (kembali ke default) lalu terapkan.</summary>
        Task ResetAsync();

        /// <summary>Muat ulang dari database ke <see cref="HealthRanges"/>.</summary>
        Task ReloadAsync();
    }

    public class HealthReferenceRepository : IHealthReferenceRepository
    {
        private class Row
        {
            public string Key { get; set; } = string.Empty;
            public decimal Value { get; set; }
        }

        private readonly IDbConnectionFactory _connectionFactory;

        public HealthReferenceRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Dictionary<string, decimal>> GetOverridesAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            var rows = await conn.QueryAsync<Row>("SELECT [Key] AS [Key], Value FROM dbo.HealthReferenceValues");
            return rows.ToDictionary(r => r.Key, r => r.Value);
        }

        public async Task SaveAsync(IReadOnlyDictionary<string, decimal> values, string updatedBy)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            foreach (var (key, value) in values)
            {
                if (HealthRanges.Definition(key) is null)
                    continue;   // key tak dikenal tidak pernah disimpan

                await conn.ExecuteAsync(@"
                    UPDATE dbo.HealthReferenceValues SET Value = @Value, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @By WHERE [Key] = @Key;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.HealthReferenceValues ([Key], Value, UpdatedAt, UpdatedBy) VALUES (@Key, @Value, SYSUTCDATETIME(), @By);",
                    new { Key = key, Value = value, By = updatedBy }, tx);
            }

            tx.Commit();
            await ReloadAsync();
        }

        public async Task ResetAsync()
        {
            using (var conn = _connectionFactory.CreateConnection())
                await conn.ExecuteAsync("DELETE FROM dbo.HealthReferenceValues");
            await ReloadAsync();
        }

        public async Task ReloadAsync() => HealthRanges.Apply(await GetOverridesAsync());
    }
}
