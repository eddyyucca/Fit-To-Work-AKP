using System.Data.Common;
using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Services;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Repositories
{
    /// <summary>Kapan pengaturan FTW terakhir diubah (untuk ditampilkan di halaman admin).</summary>
    public class FtwSettingsInfo
    {
        public DateTime? UpdatedAt { get; set; }   // UTC
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Pengaturan pengisian Fit To Work di dbo.AppSettings (key "Ftw.*").
    /// Key yang belum ada di tabel = memakai <see cref="FtwSettings.Default"/>.
    /// Dibaca setiap karyawan membuka form, jadi di-cache 60 detik; menyimpan langsung menghapus cache.
    /// </summary>
    public interface IFtwSettingRepository
    {
        Task<FtwSettings> GetAsync();
        Task<FtwSettingsInfo> GetInfoAsync();
        Task SaveAsync(FtwSettings settings, string updatedBy);

        /// <summary>Hapus semua key "Ftw.*" (kembali ke default).</summary>
        Task ResetAsync();
    }

    public class FtwSettingRepository : IFtwSettingRepository
    {
        private const string CacheKey = "ftw-settings";

        private class Row
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<FtwSettingRepository> _logger;

        public FtwSettingRepository(IDbConnectionFactory connectionFactory, IMemoryCache cache, ILogger<FtwSettingRepository> logger)
        {
            _connectionFactory = connectionFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<FtwSettings> GetAsync()
        {
            if (_cache.TryGetValue(CacheKey, out FtwSettings? cached) && cached is not null)
                return cached;

            FtwSettings settings;
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var rows = await conn.QueryAsync<Row>(
                    "SELECT [Key] AS [Key], Value FROM dbo.AppSettings WHERE [Key] IN @Keys",
                    new { Keys = FtwSettingKeys.All });
                settings = FtwRules.FromValues(rows.ToDictionary(r => r.Key, r => r.Value));
            }
            catch (DbException ex)
            {
                // Mis. migration_ftw_settings.sql belum dijalankan: form karyawan tetap jalan dengan default
                _logger.LogWarning(ex, "Pengaturan FTW tidak terbaca, memakai default");
                settings = FtwSettings.Default;
            }

            _cache.Set(CacheKey, settings, TimeSpan.FromSeconds(60));
            return settings;
        }

        public async Task<FtwSettingsInfo> GetInfoAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<FtwSettingsInfo>(
                       "SELECT TOP 1 UpdatedAt, UpdatedBy FROM dbo.AppSettings WHERE [Key] IN @Keys ORDER BY UpdatedAt DESC",
                       new { Keys = FtwSettingKeys.All })
                   ?? new FtwSettingsInfo();
        }

        public async Task SaveAsync(FtwSettings settings, string updatedBy)
        {
            using (var conn = _connectionFactory.CreateConnection())
            {
                conn.Open();
                using var tx = conn.BeginTransaction();

                foreach (var (key, value) in FtwRules.ToValues(settings))
                {
                    await conn.ExecuteAsync(@"
                        UPDATE dbo.AppSettings SET Value = @Value, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @By WHERE [Key] = @Key;
                        IF @@ROWCOUNT = 0
                            INSERT INTO dbo.AppSettings ([Key], Value, UpdatedAt, UpdatedBy) VALUES (@Key, @Value, SYSUTCDATETIME(), @By);",
                        new { Key = key, Value = value, By = updatedBy }, tx);
                }

                tx.Commit();
            }
            _cache.Remove(CacheKey);
        }

        public async Task ResetAsync()
        {
            using (var conn = _connectionFactory.CreateConnection())
                await conn.ExecuteAsync("DELETE FROM dbo.AppSettings WHERE [Key] IN @Keys", new { Keys = FtwSettingKeys.All });
            _cache.Remove(CacheKey);
        }
    }
}
