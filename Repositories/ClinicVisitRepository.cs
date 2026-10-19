using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;

namespace FitToWorkApp.Repositories
{
    public record VisitMedicineInput(int MedicineId, int Qty, string? AturanPakai);

    public interface IClinicVisitRepository
    {
        /// <summary>
        /// Simpan kunjungan + diagnosa + obat dalam 1 transaksi, sekaligus memotong stok obat.
        /// Melempar <see cref="StockException"/> kalau stok tidak cukup (tidak ada yang tersimpan).
        /// </summary>
        /// <param name="triageId">
        /// Kalau diisi, kunjungan ini memfinalkan pemeriksaan awal perawat: draft itu ditandai Selesai di TRANSAKSI YANG SAMA.
        /// Melempar <see cref="TriageStateException"/> kalau draft sudah tidak berstatus Draft (tidak ada yang tersimpan).
        /// </param>
        Task<int> CreateAsync(ClinicVisit visit, IEnumerable<int> diseaseIds, IEnumerable<VisitMedicineInput> medicines, int? triageId = null);

        Task<ClinicVisit?> GetByIdAsync(int id);

        /// <summary>Batalkan kunjungan (data tetap ada, ditandai batal) dan kembalikan stok obatnya.</summary>
        Task CancelAsync(int id, string reason, int? adminId, string adminName);

        Task<MedicalPageResult<ClinicVisit>> GetPageAsync(MedicalPageQuery q);
        Task<List<ClinicVisit>> GetForExportAsync(MedicalPageQuery q, int maxRows);
        Task<List<ClinicVisit>> GetByEmployeeAsync(int employeeId, bool includeCancelled, int top = 200);

        /// <summary>Jumlah kunjungan (yang tidak dibatalkan) seorang karyawan.</summary>
        Task<int> CountByEmployeeAsync(int employeeId);
    }

    public class ClinicVisitRepository : IClinicVisitRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ClinicVisitRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> CreateAsync(ClinicVisit v, IEnumerable<int> diseaseIds, IEnumerable<VisitMedicineInput> medicines, int? triageId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var id = await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.ClinicVisits
                        (EmployeeId, NamaSnapshot, DepartmentSnapshot, SectionSnapshot, PositionSnapshot, VisitAt,
                         Keluhan, Pemeriksaan, Rekomendasi, HariIstirahat, IstirahatDari, IstirahatSampai, CatatanRekomendasi,
                         ExaminerAdminId, ExaminerName, CreatedAt, IsCancelled)
                    OUTPUT INSERTED.Id
                    VALUES
                        (@EmployeeId, @NamaSnapshot, @DepartmentSnapshot, @SectionSnapshot, @PositionSnapshot, @VisitAt,
                         @Keluhan, @Pemeriksaan, @Rekomendasi, @HariIstirahat, @IstirahatDari, @IstirahatSampai, @CatatanRekomendasi,
                         @ExaminerAdminId, @ExaminerName, SYSUTCDATETIME(), 0)", v, tx);

                foreach (var diseaseId in diseaseIds.Distinct())
                    await conn.ExecuteAsync(
                        "INSERT INTO dbo.ClinicVisitDiseases (ClinicVisitId, DiseaseId) VALUES (@VisitId, @DiseaseId)",
                        new { VisitId = id, DiseaseId = diseaseId }, tx);

                // Obat yang sama dipilih 2x digabung jumlahnya
                var grouped = medicines.GroupBy(m => m.MedicineId)
                    .Select(g => new VisitMedicineInput(g.Key, g.Sum(x => x.Qty),
                        string.Join("; ", g.Select(x => x.AturanPakai).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())));

                foreach (var m in grouped)
                {
                    var med = await conn.QueryFirstOrDefaultAsync<Medicine>(
                        "SELECT * FROM dbo.Medicines WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id", new { Id = m.MedicineId }, tx)
                        ?? throw new StockException("Obat tidak ditemukan.");

                    if (!med.IsActive)
                        throw new StockException($"Obat {med.Name} sudah tidak aktif.");
                    if (med.Stok < m.Qty)
                        throw new StockException($"Stok {med.Name} tidak cukup (sisa {med.Stok} {med.Satuan}, diminta {m.Qty}).");

                    var stokBaru = med.Stok - m.Qty;
                    await conn.ExecuteAsync("UPDATE dbo.Medicines SET Stok = @Stok WHERE Id = @Id", new { Id = med.Id, Stok = stokBaru }, tx);

                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.ClinicVisitMedicines (ClinicVisitId, MedicineId, NamaSnapshot, SatuanSnapshot, Qty, AturanPakai)
                        VALUES (@VisitId, @MedicineId, @Nama, @Satuan, @Qty, @AturanPakai)",
                        new
                        {
                            VisitId = id, MedicineId = med.Id, Nama = med.Name, Satuan = med.Satuan, m.Qty,
                            AturanPakai = string.IsNullOrWhiteSpace(m.AturanPakai) ? null : m.AturanPakai.Trim()
                        }, tx);

                    await MedicalMasterRepository.InsertMovementAsync(conn, tx, med.Id, StockMovementTypes.Keluar, -m.Qty, stokBaru,
                        id, $"Kunjungan klinik #{id} - {v.NamaSnapshot}", v.ExaminerAdminId, v.ExaminerName);
                }

                // Memfinalkan pemeriksaan awal perawat: ditandai Selesai di transaksi yang sama (cegah kunjungan ganda dari 1 draft)
                if (triageId.HasValue)
                {
                    var n = await conn.ExecuteAsync(@"
                        UPDATE dbo.ClinicTriages
                        SET Status = 'Selesai', VisitId = @VisitId, FinalizedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
                        WHERE Id = @TriageId AND Status = 'Draft' AND EmployeeId = @EmployeeId",
                        new { VisitId = id, TriageId = triageId.Value, v.EmployeeId }, tx);
                    if (n == 0)
                        throw new TriageStateException("Pemeriksaan awal ini sudah difinalkan atau dibatalkan oleh pengguna lain. Tidak ada data yang tersimpan.");
                }

                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<ClinicVisit?> GetByIdAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(@"
                SELECT v.*, e.NIP FROM dbo.ClinicVisits v INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId WHERE v.Id = @Id;
                SELECT d.* FROM dbo.ClinicVisitDiseases vd INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                WHERE vd.ClinicVisitId = @Id ORDER BY d.Name;
                SELECT * FROM dbo.ClinicVisitMedicines WHERE ClinicVisitId = @Id ORDER BY Id;", new { Id = id });

            var visit = await multi.ReadFirstOrDefaultAsync<ClinicVisit>();
            if (visit is null)
                return null;
            visit.Diseases = (await multi.ReadAsync<Disease>()).ToList();
            visit.Medicines = (await multi.ReadAsync<ClinicVisitMedicine>()).ToList();
            visit.DiagnosaText = string.Join(", ", visit.Diseases.Select(d => d.Name));
            return visit;
        }

        public async Task CancelAsync(int id, string reason, int? adminId, string adminName)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var affected = await conn.ExecuteAsync(@"
                    UPDATE dbo.ClinicVisits
                    SET IsCancelled = 1, CancelReason = @Reason, CancelledAt = SYSUTCDATETIME(), CancelledBy = @By
                    WHERE Id = @Id AND IsCancelled = 0", new { Id = id, Reason = reason, By = adminName }, tx);
                if (affected == 0)
                    throw new StockException("Kunjungan tidak ditemukan atau sudah dibatalkan.");

                var items = await conn.QueryAsync<ClinicVisitMedicine>(
                    "SELECT * FROM dbo.ClinicVisitMedicines WHERE ClinicVisitId = @Id", new { Id = id }, tx);

                foreach (var item in items)
                {
                    var stokBaru = await conn.ExecuteScalarAsync<int>(
                        "UPDATE dbo.Medicines SET Stok = Stok + @Qty OUTPUT INSERTED.Stok WHERE Id = @MedicineId",
                        new { item.Qty, item.MedicineId }, tx);
                    await MedicalMasterRepository.InsertMovementAsync(conn, tx, item.MedicineId, StockMovementTypes.Batal, item.Qty,
                        stokBaru, id, $"Kunjungan #{id} dibatalkan: {reason}", adminId, adminName);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // Diagnosa digabung jadi satu teks untuk tampilan tabel
        private const string SelectSql = @"
            SELECT v.*, e.NIP,
                   STUFF((SELECT ', ' + d.Name FROM dbo.ClinicVisitDiseases vd
                          INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                          WHERE vd.ClinicVisitId = v.Id ORDER BY d.Name
                          FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS DiagnosaText
            FROM dbo.ClinicVisits v
            INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId";

        private static readonly string[] SortColumns =
        {
            "v.VisitAt", "e.NIP", "v.NamaSnapshot", "v.DepartmentSnapshot", "v.Keluhan", "DiagnosaText", "v.Rekomendasi"
        };

        private const string WhereSql = @"
            WHERE (@FromUtc IS NULL OR v.VisitAt >= @FromUtc)
              AND (@ToUtc   IS NULL OR v.VisitAt <  @ToUtc)
              AND (@Department IS NULL OR v.DepartmentSnapshot = @Department)
              AND (@Status IS NULL
                   OR (@Status = 'batal' AND v.IsCancelled = 1)
                   OR (@Status <> 'batal' AND v.IsCancelled = 0 AND v.Rekomendasi = @Status))
              AND (@Like IS NULL OR e.NIP LIKE @Like OR v.NamaSnapshot LIKE @Like OR v.Keluhan LIKE @Like
                   OR v.DepartmentSnapshot LIKE @Like
                   OR EXISTS (SELECT 1 FROM dbo.ClinicVisitDiseases vd INNER JOIN dbo.Diseases d ON d.Id = vd.DiseaseId
                              WHERE vd.ClinicVisitId = v.Id AND (d.Name LIKE @Like OR d.Kode LIKE @Like)))";

        private static object Params(MedicalPageQuery q) => new
        {
            q.FromUtc, q.ToUtc,
            Department = string.IsNullOrWhiteSpace(q.Department) ? null : q.Department,
            Status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status,
            Like = MedicalSql.Like(q.Search),
            Skip = Math.Max(q.Skip, 0),
            Take = Math.Clamp(q.Take, 1, 100)
        };

        private static string OrderBy(MedicalPageQuery q)
        {
            var dir = q.SortDesc ? "DESC" : "ASC";
            return $"ORDER BY {SortColumns[Math.Clamp(q.SortColumn, 0, SortColumns.Length - 1)]} {dir}, Id {dir}";
        }

        public async Task<MedicalPageResult<ClinicVisit>> GetPageAsync(MedicalPageQuery q)
        {
            // Dibungkus subquery supaya ORDER BY bisa pakai kolom hasil (DiagnosaText)
            var sql = $@"
                SELECT COUNT(1) FROM dbo.ClinicVisits;
                SELECT COUNT(1) AS Filtered, COUNT(DISTINCT v.EmployeeId) AS FilteredEmployees
                FROM dbo.ClinicVisits v INNER JOIN dbo.Employees e ON e.Id = v.EmployeeId {WhereSql};
                SELECT * FROM ({SelectSql} {WhereSql}) x
                {OrderBy(q).Replace("v.", "x.").Replace("e.NIP", "x.NIP")}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = _connectionFactory.CreateConnection();
            using var multi = await conn.QueryMultipleAsync(sql, Params(q));
            var total = await multi.ReadSingleAsync<int>();
            var counts = await multi.ReadSingleAsync<MedicalCounts>();
            return new MedicalPageResult<ClinicVisit>
            {
                Total = total,
                Filtered = counts.Filtered,
                FilteredEmployees = counts.FilteredEmployees,
                Rows = (await multi.ReadAsync<ClinicVisit>()).ToList()
            };
        }

        public async Task<List<ClinicVisit>> GetForExportAsync(MedicalPageQuery q, int maxRows)
        {
            var sql = $@"
                SELECT TOP (@MaxRows) * FROM ({SelectSql} {WhereSql}) x
                {OrderBy(q).Replace("v.", "x.").Replace("e.NIP", "x.NIP")}";
            var p = new DynamicParameters(Params(q));
            p.Add("MaxRows", maxRows);

            using var conn = _connectionFactory.CreateConnection();
            var visits = (await conn.QueryAsync<ClinicVisit>(sql, p)).ToList();
            await LoadMedicinesAsync(conn, visits);
            return visits;
        }

        public async Task<List<ClinicVisit>> GetByEmployeeAsync(int employeeId, bool includeCancelled, int top = 200)
        {
            using var conn = _connectionFactory.CreateConnection();
            var visits = (await conn.QueryAsync<ClinicVisit>($@"
                SELECT TOP (@Top) * FROM ({SelectSql}
                    WHERE v.EmployeeId = @EmployeeId AND (@IncludeCancelled = 1 OR v.IsCancelled = 0)) x
                ORDER BY x.VisitAt DESC, x.Id DESC",
                new { EmployeeId = employeeId, IncludeCancelled = includeCancelled, Top = top })).ToList();
            await LoadMedicinesAsync(conn, visits);
            return visits;
        }

        public async Task<int> CountByEmployeeAsync(int employeeId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ClinicVisits WHERE EmployeeId = @EmployeeId AND IsCancelled = 0", new { EmployeeId = employeeId });
        }

        private static async Task LoadMedicinesAsync(System.Data.IDbConnection conn, List<ClinicVisit> visits)
        {
            if (visits.Count == 0)
                return;
            // Dipecah per 1000 id karena SQL Server membatasi jumlah parameter (maks. 2100)
            var items = new List<ClinicVisitMedicine>();
            foreach (var chunk in visits.Select(v => v.Id).Chunk(1000))
                items.AddRange(await conn.QueryAsync<ClinicVisitMedicine>(
                    "SELECT * FROM dbo.ClinicVisitMedicines WHERE ClinicVisitId IN @Ids ORDER BY Id", new { Ids = chunk }));
            var byVisit = items.GroupBy(i => i.ClinicVisitId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var v in visits)
                v.Medicines = byVisit.TryGetValue(v.Id, out var list) ? list : new List<ClinicVisitMedicine>();
        }
    }
}
