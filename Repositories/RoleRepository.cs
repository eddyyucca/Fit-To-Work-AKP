using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public interface IRoleRepository
    {
        Task<List<Role>> GetAllAsync();
        Task<Role?> GetByIdAsync(int id);
        Task<Role?> GetSuperAdminRoleAsync();
        Task<bool> NameExistsAsync(string name, int? excludeId = null);
        Task<int> CreateAsync(Role role, IEnumerable<string> permissions);
        Task UpdateAsync(Role role, IEnumerable<string> permissions);
        Task DeleteAsync(int id);

        /// <summary>Role + permission milik satu akun admin (dipakai untuk cek hak akses).</summary>
        /// <summary>IsActive = false kalau akun nonaktif atau sudah dihapus.</summary>
        Task<(bool IsActive, bool IsSuperAdmin, string? RoleName, List<string> Permissions)> GetAccessForAdminAsync(int adminUserId);
    }

    public class RoleRepository : IRoleRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public RoleRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<Role>> GetAllAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT r.*, (SELECT COUNT(1) FROM dbo.AdminUsers u WHERE u.RoleId = r.Id) AS UserCount
                FROM dbo.Roles r
                ORDER BY r.IsSuperAdmin DESC, r.Name;

                SELECT RoleId, Permission FROM dbo.RolePermissions;";

            using var multi = await conn.QueryMultipleAsync(sql);
            var roles = (await multi.ReadAsync<Role>()).ToList();
            var perms = (await multi.ReadAsync<RolePermissionRow>())
                .GroupBy(p => p.RoleId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Permission).ToList());

            foreach (var r in roles)
                r.Permissions = perms.TryGetValue(r.Id, out var list) ? list : new List<string>();
            return roles;
        }

        public async Task<Role?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT r.*, (SELECT COUNT(1) FROM dbo.AdminUsers u WHERE u.RoleId = r.Id) AS UserCount
                FROM dbo.Roles r WHERE r.Id = @Id;

                SELECT Permission FROM dbo.RolePermissions WHERE RoleId = @Id;";

            using var multi = await conn.QueryMultipleAsync(sql, new { Id = id });
            var role = await multi.ReadFirstOrDefaultAsync<Role>();
            if (role is null)
                return null;
            role.Permissions = (await multi.ReadAsync<string>()).ToList();
            return role;
        }

        public async Task<Role?> GetSuperAdminRoleAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 * FROM dbo.Roles WHERE IsSuperAdmin = 1 ORDER BY Id";
            return await conn.QueryFirstOrDefaultAsync<Role>(sql);
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT COUNT(1) FROM dbo.Roles WHERE Name = @Name AND (@ExcludeId IS NULL OR Id <> @ExcludeId)";
            return await conn.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId }) > 0;
        }

        public async Task<int> CreateAsync(Role role, IEnumerable<string> permissions)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            const string sql = @"
                INSERT INTO dbo.Roles (Name, Description, IsSuperAdmin, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Description, 0, SYSUTCDATETIME())";
            var id = await conn.ExecuteScalarAsync<int>(sql, role, tx);
            await InsertPermissionsAsync(conn, tx, id, permissions);

            tx.Commit();
            return id;
        }

        public async Task UpdateAsync(Role role, IEnumerable<string> permissions)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            await conn.ExecuteAsync(
                "UPDATE dbo.Roles SET Name = @Name, Description = @Description WHERE Id = @Id", role, tx);

            // Permission Super Admin tidak disimpan per baris (selalu semua), jadi tidak diubah
            if (!role.IsSuperAdmin)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.RolePermissions WHERE RoleId = @Id", new { role.Id }, tx);
                await InsertPermissionsAsync(conn, tx, role.Id, permissions);
            }

            tx.Commit();
        }

        public async Task DeleteAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            // RolePermissions ikut terhapus (ON DELETE CASCADE)
            await conn.ExecuteAsync("DELETE FROM dbo.Roles WHERE Id = @Id AND IsSuperAdmin = 0", new { Id = id });
        }

        public async Task<(bool IsActive, bool IsSuperAdmin, string? RoleName, List<string> Permissions)> GetAccessForAdminAsync(int adminUserId)
        {
            using var conn = _connectionFactory.CreateConnection();
            // LEFT JOIN: akun tanpa role tetap terbaca (aktif, tapi tanpa akses)
            const string sql = @"
                SELECT u.IsActive, r.Id AS RoleId, r.Name AS RoleName, ISNULL(r.IsSuperAdmin, 0) AS IsSuperAdmin
                FROM dbo.AdminUsers u
                LEFT JOIN dbo.Roles r ON r.Id = u.RoleId
                WHERE u.Id = @Id;

                SELECT rp.Permission
                FROM dbo.AdminUsers u
                INNER JOIN dbo.RolePermissions rp ON rp.RoleId = u.RoleId
                WHERE u.Id = @Id;";

            using var multi = await conn.QueryMultipleAsync(sql, new { Id = adminUserId });
            var row = await multi.ReadFirstOrDefaultAsync<AccessRow>();
            var perms = (await multi.ReadAsync<string>()).ToList();

            if (row is null || !row.IsActive)
                return (false, false, null, new List<string>());   // akun dihapus / nonaktif = tidak ada akses & sesi diputus
            if (row.RoleId is null)
                return (true, false, null, new List<string>());    // belum punya role = tidak punya akses apa pun
            return (true, row.IsSuperAdmin, row.RoleName, perms);
        }

        private class AccessRow
        {
            public bool IsActive { get; set; }
            public int? RoleId { get; set; }
            public string? RoleName { get; set; }
            public bool IsSuperAdmin { get; set; }
        }

        private class RolePermissionRow
        {
            public int RoleId { get; set; }
            public string Permission { get; set; } = string.Empty;
        }

        private static async Task InsertPermissionsAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx,
            int roleId, IEnumerable<string> permissions)
        {
            var rows = permissions.Distinct().Select(p => new { RoleId = roleId, Permission = p }).ToList();
            if (rows.Count > 0)
                await conn.ExecuteAsync(
                    "INSERT INTO dbo.RolePermissions (RoleId, Permission) VALUES (@RoleId, @Permission)", rows, tx);
        }
    }
}
