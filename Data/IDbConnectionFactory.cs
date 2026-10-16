using System.Data;
using Microsoft.Data.SqlClient;

namespace FitToWorkApp.Data
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
    }

    public class SqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public SqlConnectionFactory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' tidak ditemukan di appsettings.json");
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
