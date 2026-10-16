using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public interface IAdminUserRepository
    {
        Task<AdminUser?> GetByUsernameAsync(string username);
        Task<AdminUser?> GetByIdAsync(int id);
        Task<IEnumerable<AdminUser>> GetAllAsync();
        Task<int> CreateAsync(AdminUser admin);
        Task<bool> AnyAsync();
        Task<int> CountAsync();
        Task<bool> UsernameExistsAsync(string username, int? excludeId = null);
        Task UpdateRoleAsync(int id, int roleId);
        Task<int> CountSuperAdminsAsync();
        Task UpdatePasswordAsync(int id, string passwordHash);
        Task UpdateProfileAsync(int id, string fullName, int? employeeId);
        Task SetActiveAsync(int id, bool isActive);

        /// <summary>Username akun admin LAIN yang sudah terhubung ke karyawan ini (null = belum dipakai).</summary>
        Task<string?> GetUsernameLinkedToEmployeeAsync(int employeeId, int excludeAdminId);
        Task DeleteAsync(int id);
    }

    public class AdminUserRepository : IAdminUserRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AdminUserRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Nama role ikut diambil (LEFT JOIN: user lama yang belum punya role tetap muncul)
        private const string SelectSql = @"
            SELECT u.*, r.Name AS RoleName, ISNULL(r.IsSuperAdmin, 0) AS IsSuperAdmin,
                   e.NIP AS EmployeeNip, e.Nama AS EmployeeNama
            FROM dbo.AdminUsers u
            LEFT JOIN dbo.Roles r ON r.Id = u.RoleId
            LEFT JOIN dbo.Employees e ON e.Id = u.EmployeeId";

        public async Task<AdminUser?> GetByUsernameAsync(string username)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectSql + " WHERE u.Username = @Username";
            return await conn.QueryFirstOrDefaultAsync<AdminUser>(sql, new { Username = username });
        }

        public async Task<AdminUser?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectSql + " WHERE u.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<AdminUser>(sql, new { Id = id });
        }

        public async Task<IEnumerable<AdminUser>> GetAllAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = SelectSql + " ORDER BY u.FullName";
            return await conn.QueryAsync<AdminUser>(sql);
        }

        public async Task<int> CreateAsync(AdminUser admin)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.AdminUsers (Username, PasswordHash, FullName, EmployeeId, RoleId, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Username, @PasswordHash, @FullName, @EmployeeId, @RoleId, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, admin);
        }

        public async Task<bool> AnyAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT COUNT(1) FROM dbo.AdminUsers";
            return await conn.ExecuteScalarAsync<int>(sql) > 0;
        }

        public async Task<int> CountAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT COUNT(1) FROM dbo.AdminUsers";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public async Task<bool> UsernameExistsAsync(string username, int? excludeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT COUNT(1) FROM dbo.AdminUsers
                WHERE Username = @Username AND (@ExcludeId IS NULL OR Id <> @ExcludeId)";
            return await conn.ExecuteScalarAsync<int>(sql, new { Username = username, ExcludeId = excludeId }) > 0;
        }

        public async Task UpdatePasswordAsync(int id, string passwordHash)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.AdminUsers SET PasswordHash = @PasswordHash WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, PasswordHash = passwordHash });
        }

        public async Task UpdateProfileAsync(int id, string fullName, int? employeeId)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.AdminUsers SET FullName = @FullName, EmployeeId = @EmployeeId WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, FullName = fullName, EmployeeId = employeeId });
        }

        public async Task SetActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.AdminUsers SET IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, IsActive = isActive });
        }

        public async Task<string?> GetUsernameLinkedToEmployeeAsync(int employeeId, int excludeAdminId)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 Username FROM dbo.AdminUsers WHERE EmployeeId = @EmployeeId AND Id <> @ExcludeId";
            return await conn.QueryFirstOrDefaultAsync<string?>(sql, new { EmployeeId = employeeId, ExcludeId = excludeAdminId });
        }

        public async Task UpdateRoleAsync(int id, int roleId)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.AdminUsers SET RoleId = @RoleId WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id, RoleId = roleId });
        }

        // Jumlah Super Admin AKTIF. Dipakai untuk mencegah Super Admin aktif terakhir
        // dihapus / diturunkan role-nya / dinonaktifkan (supaya selalu ada yang bisa login)
        public async Task<int> CountSuperAdminsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT COUNT(1) FROM dbo.AdminUsers u
                INNER JOIN dbo.Roles r ON r.Id = u.RoleId
                WHERE r.IsSuperAdmin = 1 AND u.IsActive = 1";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "DELETE FROM dbo.AdminUsers WHERE Id = @Id";
            await conn.ExecuteAsync(sql, new { Id = id });
        }
    }
}
