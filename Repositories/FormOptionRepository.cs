using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public interface IFormOptionRepository
    {
        // Hanya opsi aktif, terurut - dipakai untuk render form publik.
        Task<IEnumerable<string>> GetActiveValuesAsync(string groupKey);

        // Semua opsi (termasuk yang nonaktif) - dipakai untuk halaman admin.
        Task<IEnumerable<FormOption>> GetAllByGroupAsync(string groupKey);

        Task<FormOption?> GetByIdAsync(int id);
        Task<int> CreateAsync(FormOption option);
        Task UpdateAsync(FormOption option);
        Task DeleteAsync(int id);
        Task SetActiveAsync(int id, bool isActive);
    }

    public class FormOptionRepository : IFormOptionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public FormOptionRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<string>> GetActiveValuesAsync(string groupKey)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT OptionValue FROM dbo.FormOptions
                WHERE GroupKey = @GroupKey AND IsActive = 1
                ORDER BY SortOrder, Id";
            return await conn.QueryAsync<string>(sql, new { GroupKey = groupKey });
        }

        public async Task<IEnumerable<FormOption>> GetAllByGroupAsync(string groupKey)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT * FROM dbo.FormOptions
                WHERE GroupKey = @GroupKey
                ORDER BY SortOrder, Id";
            return await conn.QueryAsync<FormOption>(sql, new { GroupKey = groupKey });
        }

        public async Task<FormOption?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT * FROM dbo.FormOptions WHERE Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<FormOption>(sql, new { Id = id });
        }

        public async Task<int> CreateAsync(FormOption option)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.FormOptions (GroupKey, OptionValue, SortOrder, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@GroupKey, @OptionValue, @SortOrder, @IsActive)";
            return await conn.ExecuteScalarAsync<int>(sql, option);
        }

        public async Task UpdateAsync(FormOption option)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                UPDATE dbo.FormOptions
                SET OptionValue = @OptionValue, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id";
            await conn.ExecuteAsync(sql, option);
        }

        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "DELETE FROM dbo.FormOptions WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id });
        }

        public async Task SetActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.FormOptions SET IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, IsActive = isActive });
        }
    }
}
