using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    /// <summary>Pemeriksaan awal sudah tidak berstatus Draft (diselesaikan / dibatalkan pengguna lain) atau tidak cocok.</summary>
    public class TriageStateException : Exception
    {
        public TriageStateException(string message) : base(message) { }
    }

    public class ClinicTriageQuery
    {
        public string? Status { get; set; }          // Draft | Selesai | Batal | null = semua
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string? Search { get; set; }          // nama / NIP / keluhan
        public int Take { get; set; } = 500;
    }

    public interface IClinicTriageRepository
    {
        Task<int> CreateAsync(ClinicTriage t);

        /// <summary>Ubah draft. Hanya yang masih Draft; melempar <see cref="TriageStateException"/> kalau sudah bukan draft.</summary>
        Task UpdateDraftAsync(ClinicTriage t);

        Task<ClinicTriage?> GetByIdAsync(int id);
        Task<ClinicTriage?> GetByVisitIdAsync(int visitId);

        /// <summary>Pemeriksaan awal (Draft atau Selesai) yang berasal dari pengisian FTW ini; null kalau belum ada.</summary>
        Task<ClinicTriage?> GetActiveByFtwSubmissionAsync(int ftwSubmissionId);
        Task<List<ClinicTriage>> GetListAsync(ClinicTriageQuery q);
        Task<int> CountDraftAsync();

        /// <summary>Batalkan draft. Melempar <see cref="TriageStateException"/> kalau sudah bukan draft.</summary>
        Task CancelAsync(int id, string reason, string adminName);
    }

    public class ClinicTriageRepository : IClinicTriageRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ClinicTriageRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> CreateAsync(ClinicTriage t)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.ClinicTriages
                    (EmployeeId, FtwSubmissionId, NamaSnapshot, DepartmentSnapshot, SectionSnapshot, PositionSnapshot, TriageAt, Keluhan,
                     Sistolik, Diastolik, Nadi, Suhu, Napas, Spo2, BeratBadan, CatatanPerawat,
                     Status, NurseAdminId, NurseName, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES
                    (@EmployeeId, @FtwSubmissionId, @NamaSnapshot, @DepartmentSnapshot, @SectionSnapshot, @PositionSnapshot, @TriageAt, @Keluhan,
                     @Sistolik, @Diastolik, @Nadi, @Suhu, @Napas, @Spo2, @BeratBadan, @CatatanPerawat,
                     'Draft', @NurseAdminId, @NurseName, SYSUTCDATETIME())", t);
        }

        public async Task UpdateDraftAsync(ClinicTriage t)
        {
            using var conn = _connectionFactory.CreateConnection();
            var n = await conn.ExecuteAsync(@"
                UPDATE dbo.ClinicTriages
                SET TriageAt = @TriageAt, Keluhan = @Keluhan,
                    Sistolik = @Sistolik, Diastolik = @Diastolik, Nadi = @Nadi, Suhu = @Suhu, Napas = @Napas, Spo2 = @Spo2,
                    BeratBadan = @BeratBadan, CatatanPerawat = @CatatanPerawat, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id AND Status = 'Draft'", t);
            if (n == 0)
                throw new TriageStateException("Pemeriksaan awal ini sudah tidak berstatus draft (sudah difinalkan dokter atau dibatalkan).");
        }

        public async Task<ClinicTriage?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ClinicTriage>(@"
                SELECT t.*, e.NIP FROM dbo.ClinicTriages t
                INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId
                WHERE t.Id = @Id", new { Id = id });
        }

        public async Task<ClinicTriage?> GetByVisitIdAsync(int visitId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ClinicTriage>(@"
                SELECT TOP 1 t.*, e.NIP FROM dbo.ClinicTriages t
                INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId
                WHERE t.VisitId = @VisitId", new { VisitId = visitId });
        }

        public async Task<ClinicTriage?> GetActiveByFtwSubmissionAsync(int ftwSubmissionId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ClinicTriage>(@"
                SELECT TOP 1 t.*, e.NIP FROM dbo.ClinicTriages t
                INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId
                WHERE t.FtwSubmissionId = @Id AND t.Status IN ('Draft', 'Selesai')
                ORDER BY t.Id DESC", new { Id = ftwSubmissionId });
        }

        public async Task<List<ClinicTriage>> GetListAsync(ClinicTriageQuery q)
        {
            using var conn = _connectionFactory.CreateConnection();
            var search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
            var sql = @"
                SELECT TOP (@Take) t.*, e.NIP
                FROM dbo.ClinicTriages t
                INNER JOIN dbo.Employees e ON e.Id = t.EmployeeId
                WHERE (@Status IS NULL OR t.Status = @Status)
                  AND (@FromUtc IS NULL OR t.TriageAt >= @FromUtc)
                  AND (@ToUtc IS NULL OR t.TriageAt < @ToUtc)
                  AND (@Search IS NULL OR t.NamaSnapshot LIKE '%' + @Search + '%' OR e.NIP LIKE '%' + @Search + '%' OR t.Keluhan LIKE '%' + @Search + '%')
                ORDER BY CASE WHEN t.Status = 'Draft' THEN 0 ELSE 1 END, t.TriageAt DESC";
            return (await conn.QueryAsync<ClinicTriage>(sql, new
            {
                q.Take,
                Status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status,
                q.FromUtc,
                q.ToUtc,
                Search = search
            })).ToList();
        }

        public async Task<int> CountDraftAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dbo.ClinicTriages WHERE Status = 'Draft'");
        }

        public async Task CancelAsync(int id, string reason, string adminName)
        {
            using var conn = _connectionFactory.CreateConnection();
            var n = await conn.ExecuteAsync(@"
                UPDATE dbo.ClinicTriages
                SET Status = 'Batal', CancelReason = @Reason, CancelledAt = SYSUTCDATETIME(), CancelledBy = @By, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @Id AND Status = 'Draft'", new { Id = id, Reason = reason, By = adminName });
            if (n == 0)
                throw new TriageStateException("Pemeriksaan awal ini sudah tidak berstatus draft (sudah difinalkan dokter atau dibatalkan).");
        }
    }
}
