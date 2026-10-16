using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public interface IOrganizationRepository
    {
        // ---- Division ----
        Task<IEnumerable<Division>> GetDivisionsAsync(bool onlyActive = false);
        Task<Division?> GetDivisionByIdAsync(int id);
        Task<int> CreateDivisionAsync(Division division);
        Task UpdateDivisionAsync(Division division);
        Task SetDivisionActiveAsync(int id, bool isActive);
        Task DeleteDivisionAsync(int id);

        // ---- Department ----
        Task<IEnumerable<Department>> GetDepartmentsAsync(bool onlyActive = false);
        Task<Department?> GetDepartmentByIdAsync(int id);
        Task<int> CreateDepartmentAsync(Department department);
        Task UpdateDepartmentAsync(Department department);
        Task SetDepartmentActiveAsync(int id, bool isActive);
        Task DeleteDepartmentAsync(int id);

        // ---- Section ----
        Task<IEnumerable<Section>> GetSectionsAsync(bool onlyActive = false);
        Task<Section?> GetSectionByIdAsync(int id);
        Task<int> CreateSectionAsync(Section section);
        Task UpdateSectionAsync(Section section);
        Task SetSectionActiveAsync(int id, bool isActive);
        Task DeleteSectionAsync(int id);

        // ---- Position ----
        Task<IEnumerable<Position>> GetPositionsAsync(bool onlyActive = false);
        Task<Position?> GetPositionByIdAsync(int id);
        Task<int> CreatePositionAsync(Position position);
        Task UpdatePositionAsync(Position position);
        Task SetPositionActiveAsync(int id, bool isActive);
        Task DeletePositionAsync(int id);
    }

    public class OrganizationRepository : IOrganizationRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public OrganizationRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // ======================= Division =======================

        public async Task<IEnumerable<Division>> GetDivisionsAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM dbo.Divisions" + (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Name";
            return await conn.QueryAsync<Division>(sql);
        }

        public async Task<Division?> GetDivisionByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Division>("SELECT * FROM dbo.Divisions WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreateDivisionAsync(Division division)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.Divisions (Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @SortOrder, @IsActive, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, division);
        }

        public async Task UpdateDivisionAsync(Division division)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.Divisions SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, division);
        }

        public async Task SetDivisionActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Divisions SET IsActive = @IsActive WHERE Id = @Id", new { Id = id, IsActive = isActive });
        }

        public async Task DeleteDivisionAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Divisions WHERE Id = @Id", new { Id = id });
        }

        // ======================= Department =======================

        public async Task<IEnumerable<Department>> GetDepartmentsAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM dbo.Departments" + (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Name";
            return await conn.QueryAsync<Department>(sql);
        }

        public async Task<Department?> GetDepartmentByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Department>("SELECT * FROM dbo.Departments WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreateDepartmentAsync(Department department)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.Departments (Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @SortOrder, @IsActive, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, department);
        }

        public async Task UpdateDepartmentAsync(Department department)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.Departments SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, department);
        }

        public async Task SetDepartmentActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Departments SET IsActive = @IsActive WHERE Id = @Id", new { Id = id, IsActive = isActive });
        }

        public async Task DeleteDepartmentAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Departments WHERE Id = @Id", new { Id = id });
        }

        // ======================= Section =======================

        public async Task<IEnumerable<Section>> GetSectionsAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = @"
                SELECT s.*, d.Name AS DepartmentName
                FROM dbo.Sections s
                INNER JOIN dbo.Departments d ON d.Id = s.DepartmentId"
                + (onlyActive ? " WHERE s.IsActive = 1" : "")
                + " ORDER BY d.SortOrder, s.SortOrder, s.Name";
            return await conn.QueryAsync<Section>(sql);
        }

        public async Task<Section?> GetSectionByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT s.*, d.Name AS DepartmentName
                FROM dbo.Sections s
                INNER JOIN dbo.Departments d ON d.Id = s.DepartmentId
                WHERE s.Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<Section>(sql, new { Id = id });
        }

        public async Task<int> CreateSectionAsync(Section section)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.Sections (DepartmentId, Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@DepartmentId, @Name, @SortOrder, @IsActive, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, section);
        }

        public async Task UpdateSectionAsync(Section section)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                UPDATE dbo.Sections
                SET DepartmentId = @DepartmentId, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id";
            await conn.ExecuteAsync(sql, section);
        }

        public async Task SetSectionActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Sections SET IsActive = @IsActive WHERE Id = @Id", new { Id = id, IsActive = isActive });
        }

        public async Task DeleteSectionAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Sections WHERE Id = @Id", new { Id = id });
        }

        // ======================= Position =======================

        public async Task<IEnumerable<Position>> GetPositionsAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM dbo.Positions" + (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Name";
            return await conn.QueryAsync<Position>(sql);
        }

        public async Task<Position?> GetPositionByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Position>("SELECT * FROM dbo.Positions WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreatePositionAsync(Position position)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO dbo.Positions (Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @SortOrder, @IsActive, SYSUTCDATETIME())";
            return await conn.ExecuteScalarAsync<int>(sql, position);
        }

        public async Task UpdatePositionAsync(Position position)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE dbo.Positions SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id";
            await conn.ExecuteAsync(sql, position);
        }

        public async Task SetPositionActiveAsync(int id, bool isActive)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Positions SET IsActive = @IsActive WHERE Id = @Id", new { Id = id, IsActive = isActive });
        }

        public async Task DeletePositionAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Positions WHERE Id = @Id", new { Id = id });
        }
    }
}