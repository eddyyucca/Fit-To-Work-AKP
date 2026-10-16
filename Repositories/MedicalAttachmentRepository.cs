using Dapper;
using FitToWorkApp.Data;
using FitToWorkApp.Models;
using FitToWorkApp.Services;

namespace FitToWorkApp.Repositories
{
    /// <summary>
    /// Lampiran hasil pemeriksaan (PDF/foto) untuk pemkes & tes urin/alkohol.
    /// Catatan di dbo.MedicalAttachments, file fisik lewat <see cref="IMedicalFileStorage"/>.
    /// </summary>
    public interface IMedicalAttachmentRepository
    {
        /// <summary>Cek semua file dulu; kembalikan daftar pesan error (kosong = semua boleh diupload).</summary>
        List<string> Validate(IEnumerable<IFormFile>? files);

        /// <summary>Simpan file ke disk + catat di database. Mengembalikan jumlah file tersimpan.</summary>
        Task<int> SaveAsync(string entityType, int entityId, IEnumerable<IFormFile>? files, string uploadedBy);

        Task<MedicalAttachment?> GetAsync(int id);
        Task<List<MedicalAttachment>> GetForEntityAsync(string entityType, int entityId);
        Task<Dictionary<int, List<MedicalAttachment>>> GetForEntitiesAsync(string entityType, IEnumerable<int> entityIds);
        Task DeleteAsync(MedicalAttachment attachment);
        Task DeleteForEntityAsync(string entityType, int entityId);

        /// <summary>Hapus file di disk yang catatannya sudah tidak ada (mis. setelah Reset Data).</summary>
        Task<int> CleanupOrphanFilesAsync(TimeSpan? minAge = null);
    }

    public class MedicalAttachmentRepository : IMedicalAttachmentRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IMedicalFileStorage _storage;

        public MedicalAttachmentRepository(IDbConnectionFactory connectionFactory, IMedicalFileStorage storage)
        {
            _connectionFactory = connectionFactory;
            _storage = storage;
        }

        public List<string> Validate(IEnumerable<IFormFile>? files)
        {
            var list = (files ?? Enumerable.Empty<IFormFile>()).Where(f => f is not null && f.Length > 0).ToList();
            var errors = new List<string>();
            if (list.Count > _storage.MaxFilesPerUpload)
                errors.Add($"Maksimal {_storage.MaxFilesPerUpload} file sekali upload.");
            errors.AddRange(list.Select(_storage.Validate).OfType<string>());
            return errors;
        }

        public async Task<int> SaveAsync(string entityType, int entityId, IEnumerable<IFormFile>? files, string uploadedBy)
        {
            var list = (files ?? Enumerable.Empty<IFormFile>()).Where(f => f is not null && f.Length > 0).ToList();
            if (list.Count == 0)
                return 0;

            using var conn = _connectionFactory.CreateConnection();
            var saved = 0;
            foreach (var file in list)
            {
                var (storedPath, contentType) = await _storage.SaveAsync(file, entityType);
                try
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.MedicalAttachments (EntityType, EntityId, FileName, StoredPath, ContentType, SizeBytes, UploadedAt, UploadedBy)
                        VALUES (@EntityType, @EntityId, @FileName, @StoredPath, @ContentType, @SizeBytes, SYSUTCDATETIME(), @UploadedBy)",
                        new
                        {
                            EntityType = entityType,
                            EntityId = entityId,
                            FileName = Truncate(Path.GetFileName(file.FileName), 255),
                            StoredPath = storedPath,
                            ContentType = contentType,
                            SizeBytes = file.Length,
                            UploadedBy = uploadedBy
                        });
                }
                catch
                {
                    _storage.Delete(storedPath);   // catatan gagal disimpan -> jangan tinggalkan file yatim
                    throw;
                }
                saved++;
            }
            return saved;
        }

        public async Task<MedicalAttachment?> GetAsync(int id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<MedicalAttachment>(
                "SELECT * FROM dbo.MedicalAttachments WHERE Id = @Id", new { Id = id });
        }

        public async Task<List<MedicalAttachment>> GetForEntityAsync(string entityType, int entityId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return (await conn.QueryAsync<MedicalAttachment>(@"
                SELECT * FROM dbo.MedicalAttachments
                WHERE EntityType = @EntityType AND EntityId = @EntityId
                ORDER BY UploadedAt, Id", new { EntityType = entityType, EntityId = entityId })).ToList();
        }

        public async Task<Dictionary<int, List<MedicalAttachment>>> GetForEntitiesAsync(string entityType, IEnumerable<int> entityIds)
        {
            var ids = entityIds.Distinct().ToList();
            var result = new Dictionary<int, List<MedicalAttachment>>();
            if (ids.Count == 0)
                return result;

            using var conn = _connectionFactory.CreateConnection();
            // Dipecah per 1000 id karena SQL Server membatasi jumlah parameter (maks. 2100)
            foreach (var chunk in ids.Chunk(1000))
            {
                var rows = await conn.QueryAsync<MedicalAttachment>(@"
                    SELECT * FROM dbo.MedicalAttachments
                    WHERE EntityType = @EntityType AND EntityId IN @Ids
                    ORDER BY UploadedAt, Id", new { EntityType = entityType, Ids = chunk });
                foreach (var a in rows)
                {
                    if (!result.TryGetValue(a.EntityId, out var list))
                        result[a.EntityId] = list = new List<MedicalAttachment>();
                    list.Add(a);
                }
            }
            return result;
        }

        public async Task DeleteAsync(MedicalAttachment attachment)
        {
            using (var conn = _connectionFactory.CreateConnection())
                await conn.ExecuteAsync("DELETE FROM dbo.MedicalAttachments WHERE Id = @Id", new { attachment.Id });
            _storage.Delete(attachment.StoredPath);
        }

        public async Task DeleteForEntityAsync(string entityType, int entityId)
        {
            List<string> paths;
            using (var conn = _connectionFactory.CreateConnection())
            {
                paths = (await conn.QueryAsync<string>(@"
                    DELETE FROM dbo.MedicalAttachments
                    OUTPUT DELETED.StoredPath
                    WHERE EntityType = @EntityType AND EntityId = @EntityId", new { EntityType = entityType, EntityId = entityId })).ToList();
            }
            foreach (var p in paths)
                _storage.Delete(p);
        }

        public async Task<int> CleanupOrphanFilesAsync(TimeSpan? minAge = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var known = (await conn.QueryAsync<string>("SELECT StoredPath FROM dbo.MedicalAttachments")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Default: file yang baru < 1 jam dilewati (bisa jadi sedang dalam proses upload)
            return _storage.CleanupOrphans(known, minAge ?? TimeSpan.FromHours(1));
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
