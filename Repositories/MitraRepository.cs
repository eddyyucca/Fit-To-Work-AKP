using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>
    /// Master perusahaan mitra kerja. Kode mitra (3 huruf) menjadi awalan NIP karyawan mitra,
    /// mis. mitra GPE + NIP 3450 = GPE-3450.
    /// </summary>
    public interface IMitraRepository
    {
        Task<List<Mitra>> GetAllAsync(bool onlyActive = false);
        Task<Mitra?> GetByIdAsync(int id);
        Task<bool> KodeExistsAsync(string kode, int? excludeId = null);

        /// <summary>
        /// Simpan mitra (baru / ubah). Kalau kode diganti, NIP semua karyawan mitra itu ikut diganti
        /// (KODE-NIP). Mengembalikan jumlah karyawan yang NIP-nya ikut diganti.
        /// </summary>
        Task<int> SaveAsync(Mitra mitra);

        Task SetActiveAsync(int id, bool active);

        /// <summary>Hapus permanen; false kalau masih punya karyawan.</summary>
        Task<bool> DeleteAsync(int id);
    }

    public class MitraRepository : IMitraRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public MitraRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private const string SelectSql = @"
            SELECT m.Id, m.Kode, m.Nama, m.Keterangan, m.IsActive, m.CreatedAt,
                   (SELECT COUNT(1) FROM dbo.Employees e WHERE e.MitraId = m.Id) AS JumlahKaryawan
            FROM dbo.Mitras m";

        public async Task<List<Mitra>> GetAllAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectSql + " WHERE (@OnlyActive = 0 OR m.IsActive = 1) ORDER BY m.Kode";
            return (await conn.QueryAsync<Mitra>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<Mitra?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Mitra>(SelectSql + " WHERE m.Id = @Id", new { Id = id });
        }

        public async Task<bool> KodeExistsAsync(string kode, int? excludeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.Mitras WHERE Kode = @Kode AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
                new { Kode = kode, ExcludeId = excludeId }) > 0;
        }

        public async Task<int> SaveAsync(Mitra mitra)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();

            if (mitra.Id == 0)
            {
                mitra.Id = await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.Mitras (Kode, Nama, Keterangan, IsActive, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@Kode, @Nama, @Keterangan, @IsActive, SYSUTCDATETIME())", mitra);
                return 0;
            }

            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync(@"
                UPDATE dbo.Mitras SET Kode = @Kode, Nama = @Nama, Keterangan = @Keterangan, IsActive = @IsActive
                WHERE Id = @Id", mitra, tx);

            // NIP karyawan mitra selalu KODE-NIP, jadi ikut disesuaikan kalau kode berubah
            var renamed = await conn.ExecuteAsync(@"
                UPDATE dbo.Employees SET NIP = @Kode + '-' + MitraNip
                WHERE MitraId = @Id AND MitraNip IS NOT NULL AND NIP <> @Kode + '-' + MitraNip", mitra, tx);

            tx.Commit();
            return renamed;
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Mitras SET IsActive = @Active WHERE Id = @Id", new { Id = id, Active = active });
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            var affected = await conn.ExecuteAsync(@"
                DELETE FROM dbo.Mitras
                WHERE Id = @Id AND NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE MitraId = @Id)", new { Id = id });
            return affected > 0;
        }
    }
}
