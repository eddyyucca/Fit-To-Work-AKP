namespace FitToWorkApp.Services
{
    /// <summary>
    /// Penyimpanan file lampiran medis (hasil pemeriksaan, hasil tes urin/alkohol) di disk server.
    /// File TIDAK disimpan di wwwroot, jadi tidak bisa dibuka langsung lewat URL; hanya lewat controller yang cek hak akses.
    ///
    /// Lokasi folder (appsettings / environment):
    ///   "App": { "UploadPath": "/app/App_Data/uploads", "UploadMaxMb": 10 }   atau   App__UploadPath=...
    /// Default: &lt;folder aplikasi&gt;/App_Data/uploads. Di Docker folder ini di-mount ke volume (lihat docker-compose.production.yml).
    /// </summary>
    public interface IMedicalFileStorage
    {
        string RootPath { get; }
        long MaxFileBytes { get; }
        int MaxFilesPerUpload { get; }
        string AcceptAttribute { get; }

        /// <summary>Pesan error kalau file tidak boleh diupload, null kalau aman.</summary>
        string? Validate(IFormFile file);

        /// <summary>Simpan file dengan nama acak. Mengembalikan path relatif (untuk disimpan di database) & content type.</summary>
        Task<(string StoredPath, string ContentType)> SaveAsync(IFormFile file, string folder);

        Stream? OpenRead(string storedPath);
        void Delete(string storedPath);

        /// <summary>Hapus file di disk yang tidak tercatat di database (mis. setelah Reset Data). Mengembalikan jumlah file terhapus.</summary>
        int CleanupOrphans(IReadOnlySet<string> knownStoredPaths, TimeSpan minAge);
    }

    public class MedicalFileStorage : IMedicalFileStorage
    {
        private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } }),                    // %PDF
            [".jpg"] = ("image/jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } }),
            [".jpeg"] = ("image/jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } }),
            [".png"] = ("image/png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } })
        };

        private readonly ILogger<MedicalFileStorage> _logger;

        public MedicalFileStorage(IConfiguration configuration, IWebHostEnvironment env, ILogger<MedicalFileStorage> logger)
        {
            _logger = logger;
            var configured = configuration["App:UploadPath"];
            RootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
                : configured);
            var maxMb = int.TryParse(configuration["App:UploadMaxMb"], out var mb) && mb > 0 ? mb : 10;
            MaxFileBytes = maxMb * 1024L * 1024L;
        }

        public string RootPath { get; }
        public long MaxFileBytes { get; }
        public int MaxFilesPerUpload => 10;
        public string AcceptAttribute => ".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png";

        public string? Validate(IFormFile file)
        {
            var name = Path.GetFileName(file.FileName ?? string.Empty);
            if (file.Length <= 0)
                return $"File \"{name}\" kosong.";
            if (file.Length > MaxFileBytes)
                return $"File \"{name}\" terlalu besar (maks. {MaxFileBytes / 1024 / 1024} MB).";

            var ext = Path.GetExtension(name);
            if (!Allowed.TryGetValue(ext, out var info))
                return $"File \"{name}\" tidak didukung. Hanya PDF, JPG atau PNG.";

            // Cek isi file (bukan hanya ekstensi) supaya file lain yang diganti namanya tidak bisa masuk
            var header = new byte[8];
            using (var s = file.OpenReadStream())
            {
                var read = s.Read(header, 0, header.Length);
                if (!info.Signatures.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig)))
                    return $"Isi file \"{name}\" bukan {ext.TrimStart('.').ToUpperInvariant()} yang valid.";
            }
            return null;
        }

        public async Task<(string StoredPath, string ContentType)> SaveAsync(IFormFile file, string folder)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";
            var contentType = Allowed[ext].ContentType;

            // Nama file acak + folder per bulan, mis. Checkup/2026/09/5f2c...e1.pdf
            var now = DateTime.UtcNow;
            var relative = Path.Combine(SafeSegment(folder), now.ToString("yyyy"), now.ToString("MM"), $"{Guid.NewGuid():N}{ext}")
                .Replace('\\', '/');
            var full = Resolve(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            await using (var target = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
                await file.CopyToAsync(target);

            return (relative, contentType);
        }

        public Stream? OpenRead(string storedPath)
        {
            var full = Resolve(storedPath);
            return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        }

        public void Delete(string storedPath)
        {
            try
            {
                var full = Resolve(storedPath);
                if (File.Exists(full))
                    File.Delete(full);
            }
            catch (Exception ex)
            {
                // Gagal hapus file tidak boleh menggagalkan proses; sisa file dibersihkan CleanupOrphans
                _logger.LogWarning(ex, "Gagal menghapus file lampiran {Path}", storedPath);
            }
        }

        public int CleanupOrphans(IReadOnlySet<string> knownStoredPaths, TimeSpan minAge)
        {
            if (!Directory.Exists(RootPath))
                return 0;

            var known = new HashSet<string>(knownStoredPaths.Select(p => p.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            var deleted = 0;
            foreach (var full in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RootPath, full).Replace('\\', '/');
                if (known.Contains(relative))
                    continue;
                // File yang baru saja ditulis dilewati (bisa jadi sedang dalam proses simpan)
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(full) < minAge)
                    continue;
                try
                {
                    File.Delete(full);
                    deleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menghapus file yatim {Path}", full);
                }
            }
            return deleted;
        }

        /// <summary>Path relatif -> path lengkap, dan pastikan tetap di dalam folder upload.</summary>
        private string Resolve(string storedPath)
        {
            var full = Path.GetFullPath(Path.Combine(RootPath, storedPath.Replace('/', Path.DirectorySeparatorChar)));
            var root = RootPath.EndsWith(Path.DirectorySeparatorChar) ? RootPath : RootPath + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path lampiran tidak valid.");
            return full;
        }

        private static string SafeSegment(string s) =>
            new string(s.Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } clean ? clean : "Lainnya";
    }
}
