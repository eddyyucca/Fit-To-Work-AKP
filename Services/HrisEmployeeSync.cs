using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using FitToWorkApp.Data;

namespace FitToWorkApp.Services
{
    /// <summary>Satu karyawan dari REST API HRIS (GET /api/integrations/employees).</summary>
    public class HrisEmployee
    {
        [JsonPropertyName("employee_no")] public string? EmployeeNo { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("phone")] public string? Phone { get; set; }
        [JsonPropertyName("gender")] public string? Gender { get; set; }
        [JsonPropertyName("birth_date")] public string? BirthDate { get; set; }
        [JsonPropertyName("division")] public string? Division { get; set; }
        [JsonPropertyName("department")] public string? Department { get; set; }
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    }

    public class HrisSyncResult
    {
        public bool DryRun { get; set; }
        public int FromHris { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public List<string> Notes { get; } = new();
    }

    /// <summary>Gagal menghubungi HRIS / data HRIS tidak layak dipakai (sinkronisasi dibatalkan, tidak ada data yang berubah).</summary>
    public class HrisSyncException : Exception
    {
        public HrisSyncException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Menyelaraskan karyawan AKP dengan HRIS (HRIS = sumber kebenaran, FitToWork hanya membaca).
    ///  - NIP = employee_no HRIS; karyawan baru yang tidak aktif di HRIS dilewati
    ///  - nama, no HP, division / department / position mengikuti HRIS (master yang belum ada dibuat otomatis)
    ///  - tanggal lahir & jenis kelamin hanya diisi kalau HRIS punya nilainya (nilai lama tidak dikosongkan)
    ///  - aktif / nonaktif mengikuti HRIS
    ///  - karyawan mitra dan karyawan yang tidak ada di HRIS tidak disentuh; password Riwayat Saya tidak disentuh
    /// Konfigurasi (env): HRIS_API_URL (mis. https://hris.example.com) dan HRIS_API_TOKEN, sama seperti aplikasi AKPSatu lain.
    /// Semua perubahan dalam satu transaksi; dry-run menjalankan logika yang sama lalu membatalkannya.
    /// </summary>
    public interface IHrisEmployeeSync
    {
        bool IsConfigured { get; }
        Task<HrisSyncResult> RunAsync(bool dryRun, CancellationToken ct = default, string trigger = "manual");
    }

    public class HrisEmployeeSync : IHrisEmployeeSync
    {
        public const string HttpClientName = "hris";
        private const int PerPage = 500;
        private const int MaxPages = 200;

        private class ExistingRow
        {
            public int Id { get; set; }
            public string NIP { get; set; } = string.Empty;
            public string Nama { get; set; } = string.Empty;
            public string? NomorHp { get; set; }
            public int? DivisionId { get; set; }
            public int? DepartmentId { get; set; }
            public int? PositionId { get; set; }
            public DateTime? TanggalLahir { get; set; }
            public string? JenisKelamin { get; set; }
            public bool IsActive { get; set; }
            public int? MitraId { get; set; }
        }

        private class OrgRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly string _apiUrl;
        private readonly string _apiToken;
        private readonly HrisSyncStatus _status;

        // Satu sinkronisasi pada satu waktu (otomatis tiap jam dan tombol manual tidak boleh saling menimpa).
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public HrisEmployeeSync(IHttpClientFactory httpClientFactory, IDbConnectionFactory connectionFactory, IConfiguration configuration, HrisSyncStatus status)
        {
            _status = status;
            _httpClientFactory = httpClientFactory;
            _connectionFactory = connectionFactory;
            _apiUrl = (configuration["HRIS_API_URL"] ?? string.Empty).Trim().TrimEnd('/');
            _apiToken = (configuration["HRIS_API_TOKEN"] ?? string.Empty).Trim();
        }

        public bool IsConfigured => _apiUrl.Length > 0 && _apiToken.Length > 0;

        public async Task<HrisSyncResult> RunAsync(bool dryRun, CancellationToken ct = default, string trigger = "manual")
        {
            if (!IsConfigured)
                throw new HrisSyncException("HRIS belum dikonfigurasi. Isi HRIS_API_URL dan HRIS_API_TOKEN di environment aplikasi.");

            await Gate.WaitAsync(ct);
            try
            {
                var result = await RunCoreAsync(dryRun, ct);
                if (!dryRun) _status.RecordSuccess(trigger, result);
                return result;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (!dryRun) _status.RecordFailure(trigger, ex.Message);
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<HrisSyncResult> RunCoreAsync(bool dryRun, CancellationToken ct)
        {
            if (!IsConfigured)
                throw new HrisSyncException("HRIS belum dikonfigurasi. Isi HRIS_API_URL dan HRIS_API_TOKEN di environment aplikasi.");

            var fetched = await FetchAllAsync(ct);
            var employees = fetched
                .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeNo) && !string.IsNullOrWhiteSpace(e.Name))
                .GroupBy(e => e.EmployeeNo!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            // Cegah kesalahan massal: HRIS kosong = dianggap gagal, bukan "semua karyawan hilang"
            if (employees.Count == 0)
                throw new HrisSyncException("HRIS tidak mengembalikan data karyawan; sinkronisasi dibatalkan.");

            var result = new HrisSyncResult { DryRun = dryRun, FromHris = employees.Count };

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            var divisions = await LoadMasterAsync(conn, tx, "dbo.Divisions");
            var departments = await LoadMasterAsync(conn, tx, "dbo.Departments");
            var positions = await LoadMasterAsync(conn, tx, "dbo.Positions");
            var createdOrg = new List<string>();

            var existing = new Dictionary<string, ExistingRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in employees.Select(e => e.EmployeeNo!.Trim()).Chunk(1000))
            {
                var rows = await conn.QueryAsync<ExistingRow>(@"
                    SELECT Id, NIP, Nama, NomorHp, DivisionId, DepartmentId, PositionId, TanggalLahir, JenisKelamin, IsActive, MitraId
                    FROM dbo.Employees WHERE NIP IN @Nips", new { Nips = chunk }, tx);
                foreach (var r in rows) existing[r.NIP] = r;
            }

            foreach (var e in employees)
            {
                var nip = Truncate(e.EmployeeNo!.Trim(), 20)!;
                var nama = Truncate(e.Name!.Trim(), 150)!;
                var hp = Truncate(string.IsNullOrWhiteSpace(e.Phone) ? null : e.Phone.Trim(), 20);
                var gender = ParseGender(e.Gender);
                var birth = ParseDate(e.BirthDate);

                if (existing.TryGetValue(nip, out var row))
                {
                    if (row.MitraId is not null)
                    {
                        result.Skipped++;
                        result.Notes.Add($"{nip}: NIP ini milik karyawan mitra, dilewati");
                        continue;
                    }

                    var divisionId = await ResolveOrgAsync(conn, tx, "dbo.Divisions", "Division", divisions, e.Division, createdOrg) ?? row.DivisionId;
                    var departmentId = await ResolveOrgAsync(conn, tx, "dbo.Departments", "Department", departments, e.Department, createdOrg) ?? row.DepartmentId;
                    var positionId = await ResolveOrgAsync(conn, tx, "dbo.Positions", "Position", positions, e.Position, createdOrg) ?? row.PositionId;
                    var newBirth = birth ?? row.TanggalLahir;
                    var newGender = gender ?? row.JenisKelamin;

                    var changed = row.Nama != nama || row.NomorHp != hp
                        || row.DivisionId != divisionId || row.DepartmentId != departmentId || row.PositionId != positionId
                        || row.TanggalLahir?.Date != newBirth?.Date || row.JenisKelamin != newGender
                        || row.IsActive != e.IsActive;
                    if (!changed)
                    {
                        result.Unchanged++;
                        continue;
                    }

                    await conn.ExecuteAsync(@"
                        UPDATE dbo.Employees
                        SET Nama = @Nama, NomorHp = @Hp, DivisionId = @DivisionId, DepartmentId = @DepartmentId, PositionId = @PositionId,
                            TanggalLahir = @Birth, JenisKelamin = @Gender, IsActive = @IsActive
                        WHERE Id = @Id",
                        new { Nama = nama, Hp = hp, DivisionId = divisionId, DepartmentId = departmentId, PositionId = positionId, Birth = newBirth, Gender = newGender, e.IsActive, row.Id }, tx);
                    result.Updated++;
                }
                else
                {
                    if (!e.IsActive)
                    {
                        result.Skipped++;
                        continue;
                    }

                    var divisionId = await ResolveOrgAsync(conn, tx, "dbo.Divisions", "Division", divisions, e.Division, createdOrg);
                    var departmentId = await ResolveOrgAsync(conn, tx, "dbo.Departments", "Department", departments, e.Department, createdOrg);
                    var positionId = await ResolveOrgAsync(conn, tx, "dbo.Positions", "Position", positions, e.Position, createdOrg);

                    await conn.ExecuteAsync(@"
                        INSERT INTO dbo.Employees (NIP, Nama, NomorHp, DivisionId, DepartmentId, PositionId, TanggalLahir, JenisKelamin, IsActive, CreatedAt)
                        VALUES (@NIP, @Nama, @Hp, @DivisionId, @DepartmentId, @PositionId, @Birth, @Gender, 1, SYSUTCDATETIME())",
                        new { NIP = nip, Nama = nama, Hp = hp, DivisionId = divisionId, DepartmentId = departmentId, PositionId = positionId, Birth = birth, Gender = gender }, tx);
                    result.Created++;
                }
            }

            if (createdOrg.Count > 0)
                result.Notes.Insert(0, "Data organisasi baru dari HRIS: " + string.Join(", ", createdOrg));

            if (dryRun) tx.Rollback();
            else tx.Commit();
            return result;
        }

        private async Task<List<HrisEmployee>> FetchAllAsync(CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(300);   // HRIS besar atau jaringan lambat: beri waktu lebih lama
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var all = new List<HrisEmployee>();
            var page = 1;
            var lastPage = 1;
            do
            {
                var url = $"{_apiUrl}/api/integrations/employees?per_page={PerPage}&page={page}";
                try
                {
                    using var response = await client.GetAsync(url, ct);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new HrisSyncException("HRIS menolak token (401). Periksa HRIS_API_TOKEN, harus sama dengan HRIS_API_TOKEN di HRIS.");
                    if (!response.IsSuccessStatusCode)
                        throw new HrisSyncException($"HRIS membalas {(int)response.StatusCode} untuk {url}.");

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                        throw new HrisSyncException("Format respons HRIS tidak dikenali (tidak ada \"data\").");

                    foreach (var item in data.EnumerateArray())
                    {
                        var emp = item.Deserialize<HrisEmployee>();
                        if (emp is not null) all.Add(emp);
                    }

                    if (doc.RootElement.TryGetProperty("meta", out var meta) && meta.TryGetProperty("last_page", out var lp) && lp.TryGetInt32(out var n))
                        lastPage = n;
                }
                catch (HttpRequestException ex)
                {
                    throw new HrisSyncException($"HRIS tidak bisa dihubungi di {_apiUrl}. {ex.Message}", ex);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new HrisSyncException($"HRIS tidak merespons (timeout) di {_apiUrl}.", ex);
                }
                catch (JsonException ex)
                {
                    throw new HrisSyncException("Respons HRIS bukan JSON yang valid.", ex);
                }

                page++;
            } while (page <= lastPage && page <= MaxPages);

            return all;
        }

        private static async Task<Dictionary<string, OrgRow>> LoadMasterAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, string table)
        {
            // Nama tabel dari konstanta di atas (bukan input pengguna)
            var rows = await conn.QueryAsync<OrgRow>($"SELECT Id, Name, IsActive FROM {table}", transaction: tx);
            var map = new Dictionary<string, OrgRow>();
            foreach (var r in rows.OrderByDescending(r => r.IsActive).ThenBy(r => r.Id))
                map.TryAdd(EmployeeImportRules.Key(r.Name), r);   // nama kembar: pilih yang aktif / paling awal
            return map;
        }

        /// <summary>Id master untuk nama dari HRIS; dibuat kalau belum ada, diaktifkan lagi kalau nonaktif. Null kalau nama kosong.</summary>
        private static async Task<int?> ResolveOrgAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, string table, string label,
            Dictionary<string, OrgRow> cache, string? name, List<string> createdOrg)
        {
            var clean = Truncate(string.IsNullOrWhiteSpace(name) ? null : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), 150);
            if (clean is null) return null;

            var key = EmployeeImportRules.Key(clean);
            if (cache.TryGetValue(key, out var found))
            {
                if (!found.IsActive)
                {
                    await conn.ExecuteAsync($"UPDATE {table} SET IsActive = 1 WHERE Id = @Id", new { found.Id }, tx);
                    found.IsActive = true;
                }
                return found.Id;
            }

            var id = await conn.ExecuteScalarAsync<int>($@"
                INSERT INTO {table} (Name, SortOrder, IsActive, CreatedAt)
                OUTPUT INSERTED.Id
                SELECT @Name, ISNULL(MAX(SortOrder), 0) + 1, 1, SYSUTCDATETIME() FROM {table}", new { Name = clean }, tx);
            cache[key] = new OrgRow { Id = id, Name = clean, IsActive = true };
            createdOrg.Add($"{label} \"{clean}\"");
            return id;
        }

        private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

        private static string? ParseGender(string? g)
        {
            var v = g?.Trim().ToUpperInvariant();
            return v is "L" or "P" ? v : null;
        }

        private static DateTime? ParseDate(string? d) =>
            DateTime.TryParseExact(d?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
    }
}
