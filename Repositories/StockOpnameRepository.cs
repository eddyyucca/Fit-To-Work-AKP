using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;
using Microsoft.Data.SqlClient;

namespace FitToWorkApp.Repositories
{
    public record OpnameCountInput(int ItemId, int? StokFisik, string? Catatan);

    public class OpnameApplyResult
    {
        public int Diterapkan { get; set; }              // item yang selisihnya sudah diterapkan (termasuk selisih 0)
        public int Berubah { get; set; }                 // item yang stoknya benar-benar berubah
        public int TotalSelisih { get; set; }            // jumlah bersih perubahan stok (+/-)
        public List<string> Gagal { get; } = new();
    }

    /// <summary>
    /// Stok opname per periode, bisa diisi parsial. Stok obat HANYA berubah saat selisih diterapkan (Apply), dan tiap perubahan
    /// tercatat di kartu stok sebagai "Penyesuaian / opname". Selisih diterapkan secara RELATIF (fisik - stok sistem saat dihitung),
    /// jadi kunjungan klinik yang terjadi setelah penghitungan tidak tertimpa.
    /// </summary>
    public interface IStockOpnameRepository
    {
        /// <summary>False kalau tabel opname belum dibuat (migration_stock_opname.sql belum dijalankan).</summary>
        Task<bool> IsAvailableAsync();

        Task<List<StockOpname>> GetListAsync();
        Task<StockOpname?> GetByIdAsync(int id);
        Task<List<StockOpnameItem>> GetItemsAsync(int opnameId);
        Task<List<Medicine>> GetMedicinesNotInAsync(int opnameId);

        /// <summary>Buat opname + baris obat. Melempar <see cref="StockException"/> kalau periode yang sama sudah punya opname aktif.</summary>
        Task<int> CreateAsync(StockOpname opname, IEnumerable<int> medicineIds);
        Task<int> AddMedicinesAsync(int opnameId, IEnumerable<int> medicineIds);

        /// <summary>Simpan hitungan (parsial). Hanya baris yang berubah yang diperbarui; tanggal pengisian baris yang tidak berubah tetap.</summary>
        Task<int> SaveCountsAsync(int opnameId, IEnumerable<OpnameCountInput> inputs, string adminName);

        /// <summary>Terapkan selisih ke stok. itemIds null = semua yang sudah dihitung dan belum diterapkan.</summary>
        Task<OpnameApplyResult> ApplyAsync(int opnameId, IEnumerable<int>? itemIds, int? adminId, string adminName);

        Task FinishAsync(int opnameId, string adminName);
        Task CancelAsync(int opnameId);

        Task<Dictionary<int, MedicineLastOpname>> GetLastOpnameByMedicineAsync();
    }

    public class StockOpnameRepository : IStockOpnameRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public StockOpnameRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // 207 = kolom tidak ada, 208 = tabel tidak ada
        private static bool IsMissing(SqlException ex) => ex.Number is 207 or 208;

        public async Task<bool> IsAvailableAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                SELECT CASE WHEN OBJECT_ID('dbo.StockOpnames', 'U') IS NOT NULL AND OBJECT_ID('dbo.StockOpnameItems', 'U') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        }

        private const string OpnameSelect = @"
            SELECT o.*,
                   (SELECT COUNT(1) FROM dbo.StockOpnameItems i WHERE i.OpnameId = o.Id) AS TotalItem,
                   (SELECT COUNT(1) FROM dbo.StockOpnameItems i WHERE i.OpnameId = o.Id AND i.StokFisik IS NOT NULL) AS Dihitung,
                   (SELECT COUNT(1) FROM dbo.StockOpnameItems i WHERE i.OpnameId = o.Id AND i.AppliedAt IS NOT NULL) AS Diterapkan
            FROM dbo.StockOpnames o";

        public async Task<List<StockOpname>> GetListAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<StockOpname>(OpnameSelect + " ORDER BY o.PeriodeMulai DESC, o.Id DESC")).ToList();
        }

        public async Task<StockOpname?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<StockOpname>(OpnameSelect + " WHERE o.Id = @Id", new { Id = id });
        }

        public async Task<List<StockOpnameItem>> GetItemsAsync(int opnameId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<StockOpnameItem>(@"
                SELECT i.*, m.Stok AS StokSekarang, m.StokMinimum, m.IsActive AS MedicineActive
                FROM dbo.StockOpnameItems i
                INNER JOIN dbo.Medicines m ON m.Id = i.MedicineId
                WHERE i.OpnameId = @Id
                ORDER BY i.NamaSnapshot", new { Id = opnameId })).ToList();
        }

        public async Task<List<Medicine>> GetMedicinesNotInAsync(int opnameId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<Medicine>(@"
                SELECT m.* FROM dbo.Medicines m
                WHERE m.IsActive = 1 AND NOT EXISTS (SELECT 1 FROM dbo.StockOpnameItems i WHERE i.OpnameId = @Id AND i.MedicineId = m.Id)
                ORDER BY m.Name", new { Id = opnameId })).ToList();
        }

        private static async Task<int> InsertItemsAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, int opnameId, IEnumerable<int> medicineIds)
        {
            return await conn.ExecuteAsync(@"
                INSERT INTO dbo.StockOpnameItems (OpnameId, MedicineId, NamaSnapshot, SatuanSnapshot)
                SELECT @OpnameId, m.Id, m.Name, m.Satuan
                FROM dbo.Medicines m
                WHERE m.Id IN @Ids AND NOT EXISTS (SELECT 1 FROM dbo.StockOpnameItems i WHERE i.OpnameId = @OpnameId AND i.MedicineId = m.Id)",
                new { OpnameId = opnameId, Ids = medicineIds.Distinct().ToArray() }, tx);
        }

        public async Task<int> CreateAsync(StockOpname o, IEnumerable<int> medicineIds)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var dup = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.StockOpnames WHERE PeriodeMulai = @Mulai AND PeriodeSelesai = @Selesai AND Status <> 'Batal'",
                    new { Mulai = o.PeriodeMulai.Date, Selesai = o.PeriodeSelesai.Date }, tx);
                if (dup > 0)
                    throw new StockException("Periode ini sudah punya stok opname. Buka yang sudah ada dan lanjutkan pengisiannya (bisa parsial).");

                var id = await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.StockOpnames (PeriodeLabel, PeriodeMulai, PeriodeSelesai, Catatan, CreatedBy, CreatedAt)
                    OUTPUT INSERTED.Id
                    VALUES (@PeriodeLabel, @Mulai, @Selesai, @Catatan, @CreatedBy, SYSUTCDATETIME())",
                    new { o.PeriodeLabel, Mulai = o.PeriodeMulai.Date, Selesai = o.PeriodeSelesai.Date, o.Catatan, o.CreatedBy }, tx);

                var n = await InsertItemsAsync(conn, tx, id, medicineIds);
                if (n == 0)
                    throw new StockException("Pilih minimal satu obat.");

                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<int> AddMedicinesAsync(int opnameId, IEnumerable<int> medicineIds)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            var status = await conn.ExecuteScalarAsync<string?>("SELECT Status FROM dbo.StockOpnames WHERE Id = @Id", new { Id = opnameId }, tx);
            if (status != OpnameStatuses.Berjalan)
                throw new StockException("Opname ini sudah ditutup.");
            var n = await InsertItemsAsync(conn, tx, opnameId, medicineIds);
            tx.Commit();
            return n;
        }

        public async Task<int> SaveCountsAsync(int opnameId, IEnumerable<OpnameCountInput> inputs, string adminName)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var status = await conn.ExecuteScalarAsync<string?>("SELECT Status FROM dbo.StockOpnames WHERE Id = @Id", new { Id = opnameId }, tx);
                if (status != OpnameStatuses.Berjalan)
                    throw new StockException("Opname ini sudah ditutup; hitungan tidak bisa diubah.");

                var changed = 0;
                foreach (var inp in inputs)
                {
                    if (inp.StokFisik is < 0)
                        throw new StockException("Stok fisik tidak boleh negatif.");

                    var item = await conn.QueryFirstOrDefaultAsync<StockOpnameItem>(@"
                        SELECT i.*, m.Stok AS StokSekarang FROM dbo.StockOpnameItems i
                        INNER JOIN dbo.Medicines m ON m.Id = i.MedicineId
                        WHERE i.Id = @Id AND i.OpnameId = @OpnameId", new { Id = inp.ItemId, OpnameId = opnameId }, tx);
                    if (item is null || item.IsApplied)
                        continue;   // baris yang sudah diterapkan terkunci

                    var catatan = string.IsNullOrWhiteSpace(inp.Catatan) ? null : inp.Catatan.Trim();
                    if (catatan is { Length: > 250 }) catatan = catatan[..250];

                    if (inp.StokFisik is null)
                    {
                        // Dikosongkan: batalkan hitungan yang belum diterapkan; kalau memang belum dihitung, hanya catatan yang disimpan
                        if (item.IsCounted)
                        {
                            await conn.ExecuteAsync(@"UPDATE dbo.StockOpnameItems SET StokFisik = NULL, StokSistem = NULL, CountedAt = NULL, CountedBy = NULL, Catatan = @Catatan WHERE Id = @Id",
                                new { item.Id, Catatan = catatan }, tx);
                            changed++;
                        }
                        else if (catatan != item.Catatan)
                        {
                            await conn.ExecuteAsync("UPDATE dbo.StockOpnameItems SET Catatan = @Catatan WHERE Id = @Id", new { item.Id, Catatan = catatan }, tx);
                        }
                        continue;
                    }

                    // Stok fisik sama: tanggal pengisian & stok sistem saat dihitung DIPERTAHANKAN (hanya catatan yang boleh berubah).
                    // Kalau ikut disegarkan, selisih yang diterapkan nanti salah karena stok sistem sudah berubah sejak dihitung.
                    if (item.StokFisik == inp.StokFisik)
                    {
                        if (item.Catatan != catatan)
                            await conn.ExecuteAsync("UPDATE dbo.StockOpnameItems SET Catatan = @Catatan WHERE Id = @Id", new { item.Id, Catatan = catatan }, tx);
                        continue;
                    }

                    await conn.ExecuteAsync(@"
                        UPDATE dbo.StockOpnameItems
                        SET StokSistem = @Sistem, StokFisik = @Fisik, CountedAt = SYSUTCDATETIME(), CountedBy = @By, Catatan = @Catatan
                        WHERE Id = @Id",
                        new { item.Id, Sistem = item.StokSekarang, Fisik = inp.StokFisik, By = adminName, Catatan = catatan }, tx);
                    changed++;
                }

                if (changed > 0)
                    await conn.ExecuteAsync("UPDATE dbo.StockOpnames SET LastFilledAt = SYSUTCDATETIME() WHERE Id = @Id", new { Id = opnameId }, tx);

                tx.Commit();
                return changed;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<OpnameApplyResult> ApplyAsync(int opnameId, IEnumerable<int>? itemIds, int? adminId, string adminName)
        {
            var result = new OpnameApplyResult();
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();

            var opname = await conn.QueryFirstOrDefaultAsync<StockOpname>("SELECT * FROM dbo.StockOpnames WHERE Id = @Id", new { Id = opnameId })
                ?? throw new StockException("Opname tidak ditemukan.");
            if (opname.Status != OpnameStatuses.Berjalan)
                throw new StockException("Opname ini sudah ditutup.");

            var ids = itemIds?.Distinct().ToArray();
            var items = (await conn.QueryAsync<StockOpnameItem>(@"
                SELECT * FROM dbo.StockOpnameItems
                WHERE OpnameId = @OpnameId AND StokFisik IS NOT NULL AND AppliedAt IS NULL AND (@All = 1 OR Id IN @Ids)
                ORDER BY NamaSnapshot",
                new { OpnameId = opnameId, All = ids is null ? 1 : 0, Ids = ids ?? new[] { 0 } })).ToList();

            foreach (var item in items)
            {
                using var tx = conn.BeginTransaction();
                try
                {
                    // UPDLOCK: cegah kunjungan klinik mengubah stok di tengah penerapan
                    var current = await conn.ExecuteScalarAsync<int?>(
                        "SELECT Stok FROM dbo.Medicines WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id", new { Id = item.MedicineId }, tx)
                        ?? throw new StockException("obat tidak ditemukan");

                    var delta = item.StokFisik!.Value - (item.StokSistem ?? current);
                    var baru = current + delta;
                    if (baru < 0)
                        throw new StockException($"stok akan menjadi negatif ({current} {delta:+#;-#;0}); hitung ulang");

                    if (delta != 0)
                    {
                        await conn.ExecuteAsync("UPDATE dbo.Medicines SET Stok = @Stok WHERE Id = @Id", new { Id = item.MedicineId, Stok = baru }, tx);
                        await MedicalMasterRepository.InsertMovementAsync(conn, tx, item.MedicineId, StockMovementTypes.Penyesuaian, delta, baru, null,
                            $"Stok opname {opname.PeriodeLabel}: sistem {item.StokSistem ?? current} -> fisik {item.StokFisik}", adminId, adminName);
                        result.Berubah++;
                        result.TotalSelisih += delta;
                    }

                    await conn.ExecuteAsync(@"UPDATE dbo.StockOpnameItems SET AppliedAt = SYSUTCDATETIME(), AppliedDelta = @Delta, AppliedBy = @By WHERE Id = @Id",
                        new { Id = item.Id, Delta = delta, By = adminName }, tx);
                    tx.Commit();
                    result.Diterapkan++;
                }
                catch (StockException ex)
                {
                    tx.Rollback();
                    result.Gagal.Add($"{item.NamaSnapshot}: {ex.Message}");
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            return result;
        }

        public async Task FinishAsync(int opnameId, string adminName)
        {
            using var conn = _connectionFactory.CreateConnection();
            var o = await GetByIdAsync(opnameId) ?? throw new StockException("Opname tidak ditemukan.");
            if (!o.IsOpen) throw new StockException("Opname ini sudah ditutup.");
            if (o.Menunggu > 0)
                throw new StockException($"Masih ada {o.Menunggu} obat yang sudah dihitung tapi selisihnya belum diterapkan. Terapkan dulu (atau kosongkan hitungannya) sebelum menyelesaikan opname.");
            await conn.ExecuteAsync("UPDATE dbo.StockOpnames SET Status = 'Selesai', FinishedAt = SYSUTCDATETIME(), FinishedBy = @By WHERE Id = @Id AND Status = 'Berjalan'",
                new { Id = opnameId, By = adminName });
        }

        public async Task CancelAsync(int opnameId)
        {
            using var conn = _connectionFactory.CreateConnection();
            var o = await GetByIdAsync(opnameId) ?? throw new StockException("Opname tidak ditemukan.");
            if (!o.IsOpen) throw new StockException("Opname ini sudah ditutup.");
            if (o.Diterapkan > 0)
                throw new StockException("Opname yang selisihnya sudah diterapkan ke stok tidak bisa dibatalkan. Selesaikan saja.");
            await conn.ExecuteAsync("UPDATE dbo.StockOpnames SET Status = 'Batal' WHERE Id = @Id AND Status = 'Berjalan'", new { Id = opnameId });
        }

        public async Task<Dictionary<int, MedicineLastOpname>> GetLastOpnameByMedicineAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var rows = await conn.QueryAsync<MedicineLastOpname>(@"
                    SELECT x.MedicineId, x.CountedAt, x.PeriodeLabel
                    FROM (
                        SELECT i.MedicineId, i.CountedAt, o.PeriodeLabel,
                               ROW_NUMBER() OVER (PARTITION BY i.MedicineId ORDER BY i.CountedAt DESC) AS rn
                        FROM dbo.StockOpnameItems i
                        INNER JOIN dbo.StockOpnames o ON o.Id = i.OpnameId AND o.Status <> 'Batal'
                        WHERE i.CountedAt IS NOT NULL
                    ) x WHERE x.rn = 1");
                return rows.ToDictionary(r => r.MedicineId);
            }
            catch (SqlException ex) when (IsMissing(ex))
            {
                return new Dictionary<int, MedicineLastOpname>();   // migrasi belum dijalankan: halaman obat tetap jalan
            }
        }
    }
}
