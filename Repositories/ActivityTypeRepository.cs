using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Master jenis aktivitas kebugaran (dbo.ActivityTypes).</summary>
    public interface IActivityTypeRepository
    {
        /// <summary>Semua jenis + jumlah sesi yang memakainya (daftar admin).</summary>
        Task<List<ActivityType>> GetAllAsync();
        Task<List<ActivityType>> GetActiveAsync();
        Task<ActivityType?> GetByIdAsync(int id);
        Task<bool> KodeExistsAsync(string kode, int exceptId);
        Task<int> SaveAsync(ActivityType type);
        Task SetActiveAsync(int id, bool active);
        /// <summary>Hapus hanya kalau belum pernah dipakai. false = sudah dipakai (nonaktifkan saja).</summary>
        Task<bool> DeleteIfUnusedAsync(int id);
    }

    public class ActivityTypeRepository : IActivityTypeRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ActivityTypeRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<ActivityType>> GetAllAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT t.*, ISNULL(u.Jumlah, 0) AS JumlahSesi
                FROM dbo.ActivityTypes t
                LEFT JOIN (SELECT ActivityTypeId, COUNT(1) AS Jumlah FROM dbo.GymAttendances GROUP BY ActivityTypeId) u
                       ON u.ActivityTypeId = t.Id
                ORDER BY t.IsActive DESC, t.SortOrder, t.Nama";
            return (await conn.QueryAsync<ActivityType>(sql)).ToList();
        }

        public async Task<List<ActivityType>> GetActiveAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<ActivityType>(
                "SELECT * FROM dbo.ActivityTypes WHERE IsActive = 1 ORDER BY SortOrder, Nama")).ToList();
        }

        public async Task<ActivityType?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ActivityType>(
                "SELECT * FROM dbo.ActivityTypes WHERE Id = @Id", new { Id = id });
        }

        public async Task<bool> KodeExistsAsync(string kode, int exceptId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ActivityTypes WHERE Kode = @Kode AND Id <> @Id",
                new { Kode = kode, Id = exceptId }) > 0;
        }

        public async Task<int> SaveAsync(ActivityType type)
        {
            using var conn = _connectionFactory.CreateConnection();
            if (type.Id == 0)
            {
                return await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.ActivityTypes
                        (Kode, Nama, Ikon, PakaiGps, WajibBerat, MaksJam, KunciLokasi, MaksKecepatanKmh, SortOrder, IsActive, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES
                        (@Kode, @Nama, @Ikon, @PakaiGps, @WajibBerat, @MaksJam, @KunciLokasi, @MaksKecepatanKmh, @SortOrder, @IsActive, SYSUTCDATETIME())",
                    type);
            }

            // Kode tidak diubah (dipakai sebagai acuan tetap, mis. GYM)
            await conn.ExecuteAsync(@"
                UPDATE dbo.ActivityTypes
                SET Nama = @Nama, Ikon = @Ikon, PakaiGps = @PakaiGps, WajibBerat = @WajibBerat, MaksJam = @MaksJam,
                    KunciLokasi = @KunciLokasi, MaksKecepatanKmh = @MaksKecepatanKmh, SortOrder = @SortOrder,
                    IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id", type);
            return type.Id;
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE dbo.ActivityTypes SET IsActive = @Active, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
                new { Id = id, Active = active });
        }

        public async Task<bool> DeleteIfUnusedAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            var rows = await conn.ExecuteAsync(@"
                DELETE FROM dbo.ActivityTypes
                WHERE Id = @Id AND NOT EXISTS (SELECT 1 FROM dbo.GymAttendances WHERE ActivityTypeId = @Id)",
                new { Id = id });
            return rows == 1;
        }
    }
}
