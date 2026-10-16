using System.Data;
using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Services;

namespace FitToWorkApp.Repositories
{
    /// <summary>Data master yang dipakai/berubah saat import tidak lagi valid (mis. dihapus admin lain di tengah jalan).</summary>
    public class EmployeeImportException : Exception
    {
        public EmployeeImportException(string message) : base(message) { }
    }

    public class EmployeeImportCommitResult
    {
        public List<ImportEmployeeRowResult> Rows { get; } = new();
        public List<string> OrgNotes { get; } = new();
    }

    /// <summary>
    /// Import karyawan dari Excel dalam SATU transaksi: data organisasi baru / diaktifkan lagi dibuat dulu,
    /// lalu karyawan ditambah / diperbarui. Kalau ada error database, semuanya dibatalkan.
    /// </summary>
    public interface IEmployeeImportRepository
    {
        /// <summary>NIP yang sudah ada di sistem → true kalau milik karyawan mitra.</summary>
        Task<Dictionary<string, bool>> GetExistingNipsAsync(IEnumerable<string> nips);

        Task<EmployeeImportCommitResult> CommitAsync(EmployeeImportCommit commit);
    }

    public class EmployeeImportRepository : IEmployeeImportRepository
    {
        private class NipRow
        {
            public string NIP { get; set; } = string.Empty;
            public int? MitraId { get; set; }
        }

        private class ExistingRow
        {
            public int Id { get; set; }
            public int? MitraId { get; set; }
        }

        private class OrgRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        private readonly IDbConnectionFactory _connectionFactory;

        public EmployeeImportRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Nama tabel dari whitelist (bukan dari input)
        private static string Table(OrgKind kind) => kind switch
        {
            OrgKind.Division => "dbo.Divisions",
            OrgKind.Department => "dbo.Departments",
            OrgKind.Position => "dbo.Positions",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public async Task<Dictionary<string, bool>> GetExistingNipsAsync(IEnumerable<string> nips)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var list = nips.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return result;

            using var conn = _connectionFactory.CreateConnection();
            // SQL Server maks ±2100 parameter per perintah → dipecah per 1000
            foreach (var chunk in list.Chunk(1000))
            {
                var rows = await conn.QueryAsync<NipRow>("SELECT NIP, MitraId FROM dbo.Employees WHERE NIP IN @Nips", new { Nips = chunk });
                foreach (var r in rows)
                    result[r.NIP] = r.MitraId is not null;
            }
            return result;
        }

        public async Task<EmployeeImportCommitResult> CommitAsync(EmployeeImportCommit commit)
        {
            var result = new EmployeeImportCommitResult();

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            // 1. Terapkan keputusan data organisasi → (Kind, Key) = Id yang dipakai (null = dikosongkan)
            var resolved = new Dictionary<(OrgKind, string), int?>();
            foreach (var group in commit.Decisions.Values.GroupBy(d => d.Unknown.Kind).OrderBy(g => g.Key))
            {
                var label = EmployeeImportRules.Label(group.Key);
                var added = new List<string>();
                var reactivated = new List<string>();
                var mapped = new List<string>();
                var cleared = new List<string>();

                foreach (var (u, choice) in group)
                {
                    int? id;
                    switch (choice.Action)
                    {
                        case OrgResolveAction.Add:
                            id = await AddOrReuseAsync(conn, tx, u.Kind, u.Text);
                            added.Add(u.Text);
                            break;
                        case OrgResolveAction.Reactivate:
                            id = choice.MapToId;
                            var n = await conn.ExecuteAsync($"UPDATE {Table(u.Kind)} SET IsActive = 1 WHERE Id = @Id", new { Id = id }, tx);
                            if (n == 0)
                                throw new EmployeeImportException($"{label} \"{u.Text}\" sudah dihapus dari master. Upload ulang file.");
                            reactivated.Add(u.Text);
                            break;
                        case OrgResolveAction.Map:
                            var target = await conn.QueryFirstOrDefaultAsync<OrgRow>(
                                $"SELECT Id, Name, IsActive FROM {Table(u.Kind)} WHERE Id = @Id", new { Id = choice.MapToId }, tx);
                            if (target is null || !target.IsActive)
                                throw new EmployeeImportException($"{label} pengganti untuk \"{u.Text}\" sudah dihapus / dinonaktifkan. Upload ulang file.");
                            id = target.Id;
                            mapped.Add($"\"{u.Text}\" → {target.Name}");
                            break;
                        default:
                            id = null;
                            cleared.Add($"\"{u.Text}\"");
                            break;
                    }
                    resolved[(u.Kind, u.Key)] = id;
                }

                if (added.Count > 0) result.OrgNotes.Add($"{label} baru: {string.Join(", ", added)}");
                if (reactivated.Count > 0) result.OrgNotes.Add($"{label} diaktifkan lagi: {string.Join(", ", reactivated)}");
                if (mapped.Count > 0) result.OrgNotes.Add($"{label} diperbaiki: {string.Join(", ", mapped)}");
                if (cleared.Count > 0) result.OrgNotes.Add($"{label} dikosongkan: {string.Join(", ", cleared)}");
            }

            int? Resolve(EmployeeImportRow row, OrgKind kind)
            {
                if (row.IdOf(kind) is int id) return id;
                var key = EmployeeImportRules.Key(row.TextOf(kind));
                if (key.Length == 0) return null;
                if (resolved.TryGetValue((kind, key), out var r)) return r;
                throw new EmployeeImportException($"{EmployeeImportRules.Label(kind)} \"{row.TextOf(kind)}\" (baris {row.RowNumber}) belum diputuskan.");
            }

            // 2. Tambah / perbarui karyawan (baris gagal di pratinjau tidak ikut)
            foreach (var row in commit.Rows.Where(r => r.IsValid))
            {
                var rowResult = new ImportEmployeeRowResult { RowNumber = row.RowNumber, NIP = row.NIP, Nama = row.Nama };
                var p = new
                {
                    row.NIP,
                    row.Nama,
                    row.NomorHp,
                    DivisionId = Resolve(row, OrgKind.Division),
                    DepartmentId = Resolve(row, OrgKind.Department),
                    row.SectionId,
                    PositionId = Resolve(row, OrgKind.Position),
                    row.TanggalLahir,
                    row.JenisKelamin,
                    row.IsActive
                };

                // Dicek ulang di dalam transaksi (data bisa berubah sejak pratinjau)
                var existing = await conn.QueryFirstOrDefaultAsync<ExistingRow>(
                    "SELECT Id, MitraId FROM dbo.Employees WITH (UPDLOCK, HOLDLOCK) WHERE NIP = @NIP", new { row.NIP }, tx);

                if (existing is { MitraId: not null })
                {
                    rowResult.Message = "NIP ini milik karyawan mitra (kelola di menu Karyawan Mitra)";
                }
                else if (existing is null)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.Employees (NIP, Nama, NomorHp, DivisionId, DepartmentId, SectionId, PositionId, TanggalLahir, JenisKelamin, IsActive, CreatedAt)
                        VALUES (@NIP, @Nama, @NomorHp, @DivisionId, @DepartmentId, @SectionId, @PositionId, @TanggalLahir, @JenisKelamin, @IsActive, SYSUTCDATETIME())",
                        p, tx);
                    rowResult.IsSuccess = true;
                    rowResult.IsNew = true;
                    rowResult.Message = "Berhasil ditambahkan";
                }
                else
                {
                    // Tanggal lahir & jenis kelamin opsional: kalau di Excel kosong, nilai lama tetap dipakai
                    await conn.ExecuteAsync(@"
                        UPDATE dbo.Employees
                        SET Nama = @Nama, NomorHp = @NomorHp,
                            DivisionId = @DivisionId, DepartmentId = @DepartmentId,
                            SectionId = @SectionId, PositionId = @PositionId,
                            TanggalLahir = COALESCE(@TanggalLahir, TanggalLahir),
                            JenisKelamin = COALESCE(@JenisKelamin, JenisKelamin),
                            IsActive = @IsActive
                        WHERE Id = @Id",
                        new { p.Nama, p.NomorHp, p.DivisionId, p.DepartmentId, p.SectionId, p.PositionId, p.TanggalLahir, p.JenisKelamin, p.IsActive, existing.Id }, tx);
                    rowResult.IsSuccess = true;
                    rowResult.Message = "Berhasil diperbarui (NIP sudah ada)";
                }

                result.Rows.Add(rowResult);
            }

            tx.Commit();
            return result;
        }

        /// <summary>
        /// Tambah data master baru. Kalau ternyata nama yang sama sudah dibuat orang lain sejak pratinjau,
        /// pakai yang ada (dan aktifkan kalau nonaktif) supaya tidak dobel.
        /// </summary>
        private static async Task<int> AddOrReuseAsync(IDbConnection conn, IDbTransaction tx, OrgKind kind, string name)
        {
            var table = Table(kind);
            var existing = await conn.QueryFirstOrDefaultAsync<OrgRow>(
                $"SELECT TOP 1 Id, Name, IsActive FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE Name = @Name ORDER BY IsActive DESC, Id",
                new { Name = name }, tx);
            if (existing is not null)
            {
                if (!existing.IsActive)
                    await conn.ExecuteAsync($"UPDATE {table} SET IsActive = 1 WHERE Id = @Id", new { existing.Id }, tx);
                return existing.Id;
            }

            return await conn.ExecuteScalarAsync<int>($@"
                INSERT INTO {table} (Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                SELECT @Name, ISNULL(MAX(SortOrder), 0) + 1, 1, SYSUTCDATETIME() FROM {table}",
                new { Name = name }, tx);
        }
    }
}
