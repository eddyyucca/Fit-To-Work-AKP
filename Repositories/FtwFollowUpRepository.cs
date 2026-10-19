using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Follow up online tindak lanjut FTW. Tidak pernah membuat kunjungan klinik.</summary>
    public interface IFtwFollowUpRepository
    {
        /// <summary>False kalau tabel dbo.FtwFollowUps belum dibuat (migration_ftw_followup_online.sql belum dijalankan).</summary>
        Task<bool> IsAvailableAsync();
        Task<int> AddAsync(FtwFollowUpLog log);
        Task<List<FtwFollowUpLog>> GetBySubmissionAsync(int ftwSubmissionId);
    }

    public class FtwFollowUpRepository : IFtwFollowUpRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public FtwFollowUpRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<bool> IsAvailableAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID('dbo.FtwFollowUps', 'U') IS NOT NULL AND COL_LENGTH('dbo.FtwFollowUps', 'IsClosed') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        }

        public async Task<int> AddAsync(FtwFollowUpLog l)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.FtwFollowUps (FtwSubmissionId, Kanal, Hasil, Catatan, FollowUpAt, IsClosed, NextFollowUpDate, ByAdminId, ByName, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@FtwSubmissionId, @Kanal, @Hasil, @Catatan, @FollowUpAt, @IsClosed, @NextFollowUpDate, @ByAdminId, @ByName, SYSUTCDATETIME())", l);
        }

        public async Task<List<FtwFollowUpLog>> GetBySubmissionAsync(int ftwSubmissionId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<FtwFollowUpLog>(
                "SELECT * FROM dbo.FtwFollowUps WHERE FtwSubmissionId = @Id ORDER BY FollowUpAt DESC, Id DESC", new { Id = ftwSubmissionId })).ToList();
        }
    }
}
