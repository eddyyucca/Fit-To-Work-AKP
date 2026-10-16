using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>
    /// Master Kategori Penyakit & Satuan Obat.
    /// Nilai disimpan sebagai teks di Diseases.Kategori / Medicines.Satuan, jadi saat nama diganti
    /// semua data yang memakai nama lama ikut diganti (dalam 1 transaksi).
    /// </summary>
    public interface IMedicalLookupRepository
    {
        Task<List<MedicalLookup>> GetAllAsync(MedicalLookupKind kind, bool onlyActive = false);
        Task<List<string>> GetNamesAsync(MedicalLookupKind kind, bool onlyActive = true);
        Task<MedicalLookup?> GetByIdAsync(MedicalLookupKind kind, int id);
        Task<bool> NameExistsAsync(MedicalLookupKind kind, string name, int? excludeId = null);
        Task<bool> IsValidAsync(MedicalLookupKind kind, string name);   // ada & aktif

        /// <summary>Simpan (baru / ubah). Mengembalikan jumlah data pemakai yang ikut diganti namanya.</summary>
        Task<int> SaveAsync(MedicalLookupKind kind, MedicalLookup item);

        Task SetActiveAsync(MedicalLookupKind kind, int id, bool active);

        /// <summary>Hapus permanen; false kalau masih dipakai.</summary>
        Task<bool> DeleteAsync(MedicalLookupKind kind, int id);
    }

    public class MedicalLookupRepository : IMedicalLookupRepository
    {
        // Nama tabel/kolom TIDAK pernah berasal dari input pengguna (hanya dari enum ini)
        // RenameUsage = false: data pemakai adalah riwayat (snapshot), jadi TIDAK ikut diganti saat nama diubah
        private record Map(string Table, string UsageTable, string UsageColumn, bool RenameUsage = true);

        private static Map For(MedicalLookupKind kind) => kind switch
        {
            MedicalLookupKind.DiseaseCategory => new Map("dbo.DiseaseCategories", "dbo.Diseases", "Kategori"),
            MedicalLookupKind.MedicineUnit => new Map("dbo.MedicineUnits", "dbo.Medicines", "Satuan"),
            MedicalLookupKind.UrineParameter => new Map("dbo.UrineTestParameters", "dbo.DrugAlcoholTestUrineItems", "ParameterName", RenameUsage: false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private readonly IDbConnectionFactory _connectionFactory;

        public MedicalLookupRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<MedicalLookup>> GetAllAsync(MedicalLookupKind kind, bool onlyActive = false)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            var sql = $@"
                SELECT l.Id, l.Name, l.SortOrder, l.IsActive, l.CreatedAt,
                       (SELECT COUNT(1) FROM {m.UsageTable} u WHERE u.{m.UsageColumn} = l.Name) AS UsageCount
                FROM {m.Table} l
                WHERE (@OnlyActive = 0 OR l.IsActive = 1)
                ORDER BY l.SortOrder, l.Name";
            return (await conn.QueryAsync<MedicalLookup>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<List<string>> GetNamesAsync(MedicalLookupKind kind, bool onlyActive = true)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            var sql = $"SELECT Name FROM {m.Table} WHERE (@OnlyActive = 0 OR IsActive = 1) ORDER BY SortOrder, Name";
            return (await conn.QueryAsync<string>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<MedicalLookup?> GetByIdAsync(MedicalLookupKind kind, int id)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            var sql = $@"
                SELECT l.Id, l.Name, l.SortOrder, l.IsActive, l.CreatedAt,
                       (SELECT COUNT(1) FROM {m.UsageTable} u WHERE u.{m.UsageColumn} = l.Name) AS UsageCount
                FROM {m.Table} l WHERE l.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<MedicalLookup>(sql, new { Id = id });
        }

        public async Task<bool> NameExistsAsync(MedicalLookupKind kind, string name, int? excludeId = null)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM {m.Table} WHERE Name = @Name AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
                new { Name = name, ExcludeId = excludeId }) > 0;
        }

        public async Task<bool> IsValidAsync(MedicalLookupKind kind, string name)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM {m.Table} WHERE Name = @Name AND IsActive = 1", new { Name = name }) > 0;
        }

        public async Task<int> SaveAsync(MedicalLookupKind kind, MedicalLookup item)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            if (item.Id <= 0)
            {
                await conn.ExecuteAsync(
                    $"INSERT INTO {m.Table} (Name, SortOrder, IsActive, CreatedAt) VALUES (@Name, @SortOrder, @IsActive, SYSUTCDATETIME())",
                    item, tx);
                tx.Commit();
                return 0;
            }

            var oldName = await conn.ExecuteScalarAsync<string?>(
                $"SELECT Name FROM {m.Table} WITH (UPDLOCK) WHERE Id = @Id", new { item.Id }, tx);

            await conn.ExecuteAsync(
                $"UPDATE {m.Table} SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id", item, tx);

            var renamed = 0;
            if (m.RenameUsage && oldName is not null && !string.Equals(oldName, item.Name, StringComparison.Ordinal))
            {
                renamed = await conn.ExecuteAsync(
                    $"UPDATE {m.UsageTable} SET {m.UsageColumn} = @New WHERE {m.UsageColumn} = @Old",
                    new { New = item.Name, Old = oldName }, tx);
            }

            tx.Commit();
            return renamed;
        }

        public async Task SetActiveAsync(MedicalLookupKind kind, int id, bool active)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync($"UPDATE {m.Table} SET IsActive = @Active WHERE Id = @Id", new { Id = id, Active = active });
        }

        public async Task<bool> DeleteAsync(MedicalLookupKind kind, int id)
        {
            var m = For(kind);
            using var conn = _connectionFactory.CreateConnection();
            var deleted = await conn.ExecuteAsync($@"
                DELETE l FROM {m.Table} l
                WHERE l.Id = @Id
                  AND NOT EXISTS (SELECT 1 FROM {m.UsageTable} u WHERE u.{m.UsageColumn} = l.Name)", new { Id = id });
            return deleted > 0;
        }
    }
}
