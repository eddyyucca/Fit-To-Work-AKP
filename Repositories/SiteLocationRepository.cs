using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Master lokasi site (titik + radius) untuk kunci lokasi pengisian FTW & Gym.</summary>
    public interface ISiteLocationRepository
    {
        Task<List<SiteLocation>> GetAllAsync(bool onlyActive = false);
        Task<SiteLocation?> GetByIdAsync(int id);
        Task<int> SaveAsync(SiteLocation location);
        Task SetActiveAsync(int id, bool active);
        Task DeleteAsync(int id);
    }

    public class SiteLocationRepository : ISiteLocationRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SiteLocationRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<SiteLocation>> GetAllAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT Id, Nama, Latitude, Longitude, RadiusMeter, IpPublik, Keterangan, IsActive, CreatedAt, UpdatedAt
                FROM dbo.SiteLocations
                WHERE (@OnlyActive = 0 OR IsActive = 1)
                ORDER BY IsActive DESC, Nama";
            return (await conn.QueryAsync<SiteLocation>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<SiteLocation?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<SiteLocation>(
                "SELECT * FROM dbo.SiteLocations WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> SaveAsync(SiteLocation location)
        {
            using var conn = _connectionFactory.CreateConnection();
            if (location.Id == 0)
            {
                return await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.SiteLocations (Nama, Latitude, Longitude, RadiusMeter, IpPublik, Keterangan, IsActive, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@Nama, @Latitude, @Longitude, @RadiusMeter, @IpPublik, @Keterangan, @IsActive, SYSUTCDATETIME())", location);
            }

            await conn.ExecuteAsync(@"
                UPDATE dbo.SiteLocations
                SET Nama = @Nama, Latitude = @Latitude, Longitude = @Longitude, RadiusMeter = @RadiusMeter,
                    IpPublik = @IpPublik, Keterangan = @Keterangan, IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id", location);
            return location.Id;
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE dbo.SiteLocations SET IsActive = @Active, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
                new { Id = id, Active = active });
        }

        // Riwayat FTW/Gym menyimpan nama lokasi sebagai snapshot, jadi lokasi aman dihapus.
        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.SiteLocations WHERE Id = @Id", new { Id = id });
        }
    }
}
