using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public class StockException : Exception
    {
        public StockException(string message) : base(message) { }
    }

    /// <summary>Master jenis penyakit, master obat, dan mutasi stok obat.</summary>
    public interface IMedicalMasterRepository
    {
        // Jenis penyakit
        Task<List<Disease>> GetDiseasesAsync(bool onlyActive = false);
        Task<Disease?> GetDiseaseByIdAsync(int id);
        Task<bool> DiseaseNameExistsAsync(string name, int? excludeId = null);
        Task<int> SaveDiseaseAsync(Disease disease);
        Task SetDiseaseActiveAsync(int id, bool active);

        // Obat
        Task<List<Medicine>> GetMedicinesAsync(bool onlyActive = false);
        Task<Medicine?> GetMedicineByIdAsync(int id);
        Task<bool> MedicineNameExistsAsync(string name, int? excludeId = null);
        Task<int> CreateMedicineAsync(Medicine medicine, int stokAwal, int? adminId, string adminName);
        Task UpdateMedicineAsync(Medicine medicine);   // TIDAK mengubah stok
        Task SetMedicineActiveAsync(int id, bool active);

        /// <summary>Stok masuk (qty &gt; 0).</summary>
        Task<int> AddStockAsync(int medicineId, int qty, string? keterangan, int? adminId, string adminName);

        /// <summary>Stok opname: set stok ke jumlah fisik, selisihnya dicatat sebagai penyesuaian.</summary>
        Task<int> AdjustStockAsync(int medicineId, int stokFisik, string? keterangan, int? adminId, string adminName);

        Task<List<MedicineStockMovement>> GetStockCardAsync(int medicineId, int top = 500);

        /// <summary>
        /// Terapkan hasil import Excel stok obat dalam 1 transaksi (semua atau tidak sama sekali).
        /// Stok masuk ditambahkan ke stok TERKINI, opname mengganti stok, obat baru dibuat dengan stok awal.
        /// </summary>
        Task<StockImportSummary> ApplyStockImportAsync(IEnumerable<FitToWorkApp.Services.StockImportRow> rows, string fileName, int? adminId, string adminName);
    }

    public class StockImportSummary
    {
        public int Masuk { get; set; }
        public int Opname { get; set; }
        public int ObatBaru { get; set; }
        public int StokMinimum { get; set; }
    }

    public class MedicalMasterRepository : IMedicalMasterRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public MedicalMasterRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // ---------------- Jenis penyakit ----------------

        public async Task<List<Disease>> GetDiseasesAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT * FROM dbo.Diseases
                WHERE (@OnlyActive = 0 OR IsActive = 1)
                ORDER BY Kategori, Name";
            return (await conn.QueryAsync<Disease>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<Disease?> GetDiseaseByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Disease>("SELECT * FROM dbo.Diseases WHERE Id = @Id", new { Id = id });
        }

        public async Task<bool> DiseaseNameExistsAsync(string name, int? excludeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.Diseases WHERE Name = @Name AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
                new { Name = name, ExcludeId = excludeId }) > 0;
        }

        public async Task<int> SaveDiseaseAsync(Disease d)
        {
            using var conn = _connectionFactory.CreateConnection();
            if (d.Id > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE dbo.Diseases SET Kode = @Kode, Name = @Name, NameEn = @NameEn, Kategori = @Kategori, IsActive = @IsActive
                    WHERE Id = @Id", d);
                return d.Id;
            }

            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.Diseases (Kode, Name, NameEn, Kategori, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Kode, @Name, @NameEn, @Kategori, @IsActive, SYSUTCDATETIME())", d);
        }

        public async Task SetDiseaseActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Diseases SET IsActive = @Active WHERE Id = @Id", new { Id = id, Active = active });
        }

        // ---------------- Obat ----------------

        public async Task<List<Medicine>> GetMedicinesAsync(bool onlyActive = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT * FROM dbo.Medicines WHERE (@OnlyActive = 0 OR IsActive = 1) ORDER BY Name";
            return (await conn.QueryAsync<Medicine>(sql, new { OnlyActive = onlyActive })).ToList();
        }

        public async Task<Medicine?> GetMedicineByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Medicine>("SELECT * FROM dbo.Medicines WHERE Id = @Id", new { Id = id });
        }

        public async Task<bool> MedicineNameExistsAsync(string name, int? excludeId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.Medicines WHERE Name = @Name AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
                new { Name = name, ExcludeId = excludeId }) > 0;
        }

        public async Task<int> CreateMedicineAsync(Medicine m, int stokAwal, int? adminId, string adminName)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            var id = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.Medicines (Name, Satuan, Keterangan, Stok, StokMinimum, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Name, @Satuan, @Keterangan, @Stok, @StokMinimum, @IsActive, SYSUTCDATETIME())",
                new { m.Name, m.Satuan, m.Keterangan, Stok = Math.Max(stokAwal, 0), m.StokMinimum, m.IsActive }, tx);

            if (stokAwal > 0)
                await InsertMovementAsync(conn, tx, id, StockMovementTypes.Awal, stokAwal, stokAwal, null, "Stok awal", adminId, adminName);

            tx.Commit();
            return id;
        }

        public async Task UpdateMedicineAsync(Medicine m)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE dbo.Medicines
                SET Name = @Name, Satuan = @Satuan, Keterangan = @Keterangan, StokMinimum = @StokMinimum, IsActive = @IsActive
                WHERE Id = @Id", m);
        }

        public async Task SetMedicineActiveAsync(int id, bool active)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.Medicines SET IsActive = @Active WHERE Id = @Id", new { Id = id, Active = active });
        }

        public async Task<int> AddStockAsync(int medicineId, int qty, string? keterangan, int? adminId, string adminName)
        {
            if (qty <= 0)
                throw new StockException("Jumlah stok masuk harus lebih dari 0.");

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            var stokBaru = await conn.ExecuteScalarAsync<int?>(
                "UPDATE dbo.Medicines SET Stok = Stok + @Qty OUTPUT INSERTED.Stok WHERE Id = @Id",
                new { Id = medicineId, Qty = qty }, tx) ?? throw new StockException("Obat tidak ditemukan.");

            await InsertMovementAsync(conn, tx, medicineId, StockMovementTypes.Masuk, qty, stokBaru, null, keterangan, adminId, adminName);
            tx.Commit();
            return stokBaru;
        }

        public async Task<int> AdjustStockAsync(int medicineId, int stokFisik, string? keterangan, int? adminId, string adminName)
        {
            if (stokFisik < 0)
                throw new StockException("Stok fisik tidak boleh negatif.");

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            // UPDLOCK: cegah kunjungan klinik mengurangi stok di tengah proses opname
            var stokLama = await conn.ExecuteScalarAsync<int?>(
                "SELECT Stok FROM dbo.Medicines WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id", new { Id = medicineId }, tx)
                ?? throw new StockException("Obat tidak ditemukan.");

            var selisih = stokFisik - stokLama;
            if (selisih != 0)
            {
                await conn.ExecuteAsync("UPDATE dbo.Medicines SET Stok = @Stok WHERE Id = @Id", new { Id = medicineId, Stok = stokFisik }, tx);
                await InsertMovementAsync(conn, tx, medicineId, StockMovementTypes.Penyesuaian, selisih, stokFisik, null,
                    string.IsNullOrWhiteSpace(keterangan) ? $"Stok opname: {stokLama} -> {stokFisik}" : keterangan, adminId, adminName);
            }

            tx.Commit();
            return selisih;
        }

        public async Task<List<MedicineStockMovement>> GetStockCardAsync(int medicineId, int top = 500)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT TOP (@Top) * FROM dbo.MedicineStockMovements
                WHERE MedicineId = @Id
                ORDER BY CreatedAt DESC, Id DESC";
            return (await conn.QueryAsync<MedicineStockMovement>(sql, new { Id = medicineId, Top = top })).ToList();
        }

        public async Task<StockImportSummary> ApplyStockImportAsync(IEnumerable<FitToWorkApp.Services.StockImportRow> rows, string fileName,
            int? adminId, string adminName)
        {
            var summary = new StockImportSummary();
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var r in rows)
                {
                    var ket = string.IsNullOrWhiteSpace(r.Keterangan) ? $"Import Excel: {fileName}" : $"{r.Keterangan} (import Excel)";
                    if (ket.Length > 250) ket = ket[..250];

                    if (r.Status == FitToWorkApp.Services.StockImportStatus.ObatBaru)
                    {
                        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dbo.Medicines WHERE Name = @Name", new { Name = r.Nama }, tx);
                        if (exists > 0)
                            throw new StockException($"Obat '{r.Nama}' sudah ditambahkan orang lain setelah file dicek. Upload ulang file.");
                        var id = await conn.ExecuteScalarAsync<int>(@"
                            INSERT INTO dbo.Medicines (Name, Satuan, Keterangan, Stok, StokMinimum, IsActive, CreatedAt)
                            OUTPUT INSERTED.Id
                            VALUES (@Name, @Satuan, NULL, @Stok, @StokMinimum, 1, SYSUTCDATETIME())",
                            new { Name = r.Nama, r.Satuan, Stok = r.StokBaru, StokMinimum = r.StokMinimum ?? 0 }, tx);
                        if (r.StokBaru > 0)
                            await InsertMovementAsync(conn, tx, id, StockMovementTypes.Awal, r.StokBaru, r.StokBaru, null, ket, adminId, adminName);
                        summary.ObatBaru++;
                        continue;
                    }

                    if (r.MedicineId is not int medId)
                        continue;

                    // Kunci baris obat supaya tidak bentrok dengan kunjungan klinik yang sedang mengurangi stok
                    var stokKini = await conn.ExecuteScalarAsync<int?>(
                        "SELECT Stok FROM dbo.Medicines WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id", new { Id = medId }, tx)
                        ?? throw new StockException($"Obat '{r.Nama}' sudah tidak ada di sistem. Upload ulang file.");

                    if (r.Status == FitToWorkApp.Services.StockImportStatus.Masuk && r.Jumlah is > 0)
                    {
                        var stokBaru = stokKini + r.Jumlah.Value;
                        await conn.ExecuteAsync("UPDATE dbo.Medicines SET Stok = @Stok WHERE Id = @Id", new { Id = medId, Stok = stokBaru }, tx);
                        await InsertMovementAsync(conn, tx, medId, StockMovementTypes.Masuk, r.Jumlah.Value, stokBaru, null, ket, adminId, adminName);
                        summary.Masuk++;
                    }
                    else if (r.Status == FitToWorkApp.Services.StockImportStatus.Opname && r.Jumlah is int fisik)
                    {
                        var selisih = fisik - stokKini;
                        if (selisih != 0)
                        {
                            await conn.ExecuteAsync("UPDATE dbo.Medicines SET Stok = @Stok WHERE Id = @Id", new { Id = medId, Stok = fisik }, tx);
                            await InsertMovementAsync(conn, tx, medId, StockMovementTypes.Penyesuaian, selisih, fisik, null,
                                string.IsNullOrWhiteSpace(r.Keterangan) ? $"Stok opname (import Excel): {stokKini} -> {fisik}" : ket, adminId, adminName);
                        }
                        summary.Opname++;
                    }

                    if (r.UbahStokMinimum && r.StokMinimum is int min)
                    {
                        await conn.ExecuteAsync("UPDATE dbo.Medicines SET StokMinimum = @Min WHERE Id = @Id", new { Id = medId, Min = min }, tx);
                        summary.StokMinimum++;
                    }
                }

                tx.Commit();
                return summary;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        internal static Task InsertMovementAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx,
            int medicineId, string type, int qty, int stokSetelah, int? visitId, string? keterangan, int? adminId, string adminName)
        {
            return conn.ExecuteAsync(@"
                INSERT INTO dbo.MedicineStockMovements
                    (MedicineId, Type, Qty, StokSetelah, ClinicVisitId, Keterangan, AdminUserId, AdminName, CreatedAt)
                VALUES
                    (@MedicineId, @Type, @Qty, @StokSetelah, @VisitId, @Keterangan, @AdminId, @AdminName, SYSUTCDATETIME())",
                new
                {
                    MedicineId = medicineId, Type = type, Qty = qty, StokSetelah = stokSetelah, VisitId = visitId,
                    Keterangan = string.IsNullOrWhiteSpace(keterangan) ? null : keterangan.Trim(),
                    AdminId = adminId, AdminName = adminName
                }, tx);
        }
    }
}
