using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FitToWorkApp.Services
{
    public interface IAccessLogger
    {
        /// <summary>
        /// Catat satu kejadian. Username/AdminUserId diambil dari user yang login kecuali diisi manual
        /// (mis. saat login gagal). Tidak pernah melempar exception — kegagalan log tidak boleh mengganggu aplikasi.
        /// </summary>
        Task LogAsync(string eventType, string activity, int? statusCode = null,
            string? username = null, int? adminUserId = null, string? path = null);
    }

    public class AccessLogger : IAccessLogger
    {
        private readonly IAccessLogRepository _repository;
        private readonly IAdminUserRepository _adminUserRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AccessLogger> _logger;

        public AccessLogger(IAccessLogRepository repository, IAdminUserRepository adminUserRepository,
            IHttpContextAccessor httpContextAccessor, ILogger<AccessLogger> logger)
        {
            _repository = repository;
            _adminUserRepository = adminUserRepository;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task LogAsync(string eventType, string activity, int? statusCode = null,
            string? username = null, int? adminUserId = null, string? path = null)
        {
            try
            {
                var http = _httpContextAccessor.HttpContext;
                var user = http?.User;
                if (adminUserId is null && user?.IsInRole("Admin") == true
                    && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                    adminUserId = id;

                if (username is null && user?.Identity?.IsAuthenticated == true)
                {
                    username = user.FindFirstValue("Username");
                    // Cookie login lama (sebelum ada claim Username): ambil dari database
                    if (username is null && adminUserId is not null)
                        username = (await _adminUserRepository.GetByIdAsync(adminUserId.Value))?.Username;
                    username ??= user.Identity!.Name;
                }

                await _repository.InsertAsync(new AccessLog
                {
                    AdminUserId = adminUserId,
                    Username = Truncate(username ?? "(tidak dikenal)", 100)!,
                    EventType = eventType,
                    Activity = Truncate(activity, 300)!,
                    HttpMethod = http?.Request.Method,
                    Path = Truncate(path ?? (http is null ? null : http.Request.Path + http.Request.QueryString), 500),
                    StatusCode = statusCode,
                    IpAddress = Truncate(http is null ? null : GetClientIp(http), 64),
                    UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 300)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menulis access log ({EventType} {Activity})", eventType, activity);
            }
        }

        // Aplikasi berjalan di belakang Cloudflare Tunnel, jadi IP asli ada di header CF-Connecting-IP.
        // Catatan: header ini bisa dipalsukan kalau aplikasi diakses langsung tanpa lewat Cloudflare.
        public static string? GetClientIp(HttpContext http)
        {
            var cf = http.Request.Headers["CF-Connecting-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(cf))
                return cf.Trim();

            var xff = http.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(xff))
                return xff.Split(',')[0].Trim();

            return http.Connection.RemoteIpAddress?.ToString();
        }

        private static string? Truncate(string? value, int max) =>
            value is null ? null : value.Length <= max ? value : value[..max];
    }

    /// <summary>
    /// Mencatat otomatis setiap request admin yang SUDAH lolos otorisasi (buka halaman, lihat detail,
    /// export, dan semua POST/perubahan). Endpoint AJAX yang dipanggil berulang (tabel, dashboard, autocomplete)
    /// dilewati supaya log tidak penuh. Login, logout & akses ditolak dicatat terpisah di AccountController.
    /// </summary>
    public class AccessLogMiddleware
    {
        // Endpoint polling/AJAX yang terlalu sering dipanggil -> tidak dicatat (halaman induknya sudah tercatat)
        private static readonly HashSet<string> SkipActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Admin.Home", "Admin.DashboardData", "Admin.DashboardClinic", "Admin.DashboardGym", "Admin.DashboardSection", "History.Data", "GymHistory.Data", "Employees.Data",
            "Users.SearchEmployee", "AccessLogs.Data",
            "Checkups.Data", "ClinicVisits.Data", "MedicalRecord.SearchEmployee",
            "DataReset.Execute"   // dicatat sendiri di DataResetController (lengkap dengan jumlah data yang dihapus)
        };

        // Deskripsi yang mudah dibaca untuk aksi yang sering dilihat di log. Yang tidak ada di sini
        // tetap tercatat dengan deskripsi otomatis "Controller/Action".
        private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GET Admin.Dashboard"] = "Buka Dashboard",
            ["GET History.Index"] = "Buka Riwayat Pengisian FTW",
            ["GET History.DetailJson"] = "Lihat detail pengisian FTW #{id}",
            ["GET History.Detail"] = "Lihat detail pengisian FTW #{id}",
            ["GET History.Export"] = "Download Excel Riwayat FTW",
            ["GET GymHistory.Index"] = "Buka Riwayat Aktivitas Kebugaran",
            ["GET GymHistory.Export"] = "Download Excel Riwayat Aktivitas Kebugaran",
            ["GET GymHistory.Route"] = "Lihat rute aktivitas #{id}",
            ["GET ActivityTypes.Index"] = "Buka Jenis Aktivitas",
            ["GET ActivityTypes.Create"] = "Buka form tambah jenis aktivitas",
            ["GET ActivityTypes.Edit"] = "Buka form ubah jenis aktivitas #{id}",
            ["POST ActivityTypes.Save"] = "Simpan jenis aktivitas",
            ["POST ActivityTypes.ToggleActive"] = "Ubah status jenis aktivitas #{id}",
            ["POST ActivityTypes.Delete"] = "Hapus jenis aktivitas #{id}",
            ["GET Employees.Index"] = "Buka Data Karyawan",
            ["GET Employees.Get"] = "Lihat data karyawan #{id}",
            ["POST Employees.Save"] = "Simpan data karyawan",
            ["POST Employees.SetActive"] = "Ubah status aktif karyawan #{id}",
            ["POST Employees.ResetPortalPassword"] = "Reset password Riwayat Saya karyawan #{id}",
            ["POST Employees.ToggleActive"] = "Ubah status aktif karyawan #{id}",
            ["GET Employees.Import"] = "Buka halaman import karyawan",
            ["POST Employees.Import"] = "Upload file import karyawan (pratinjau)",
            ["POST Employees.ImportConfirm"] = "Simpan import karyawan dari Excel",
            ["GET Employees.DownloadTemplate"] = "Download template import karyawan",
            ["GET Employees.Export"] = "Download Excel data karyawan",
            ["POST Employees.DeleteAll"] = "HAPUS SEMUA data karyawan",
            ["GET Organization.Index"] = "Buka Data Organisasi",
            ["GET FormOptions.Index"] = "Buka Master Data Form",
            ["GET Users.Index"] = "Buka Manajemen User Admin",
            ["GET Users.Create"] = "Buka form tambah user admin",
            ["POST Users.Create"] = "Tambah user admin",
            ["POST Users.SetRole"] = "Ubah role user admin #{id}",
            ["GET Users.ChangePassword"] = "Buka ganti/reset password user #{id}",
            ["POST Users.ChangePassword"] = "Ganti/reset password user #{id}",
            ["POST Users.Delete"] = "Hapus user admin #{id}",
            ["GET Users.Edit"] = "Buka form edit user admin #{id}",
            ["POST Users.Edit"] = "Edit user admin #{id}",
            ["POST Users.ToggleActive"] = "Ubah status aktif user admin #{id}",
            ["GET Roles.Index"] = "Buka Role & Hak Akses",
            ["GET Roles.Create"] = "Buka form tambah role",
            ["GET Roles.Edit"] = "Buka form ubah role #{id}",
            ["POST Roles.Save"] = "Simpan role & hak akses",
            ["POST Roles.Delete"] = "Hapus role #{id}",
            ["GET Checkups.Index"] = "Buka Pemeriksaan Kesehatan",
            ["GET Checkups.Create"] = "Buka form input pemeriksaan",
            ["POST Checkups.Create"] = "Simpan pemeriksaan kesehatan",
            ["GET Checkups.Detail"] = "Lihat pemeriksaan kesehatan #{id}",
            ["POST Checkups.Delete"] = "Hapus pemeriksaan kesehatan #{id}",
            ["GET Checkups.Export"] = "Download Excel Pemeriksaan Kesehatan",
            ["GET Checkups.Import"] = "Buka import pemeriksaan kesehatan",
            ["GET Checkups.ImportTemplate"] = "Download template import pemeriksaan kesehatan",
            ["POST Checkups.Import"] = "Upload file import pemeriksaan kesehatan (pratinjau)",
            ["POST Checkups.ImportConfirm"] = "Simpan import pemeriksaan kesehatan dari Excel",
            ["GET ClinicVisits.Index"] = "Buka Kunjungan Klinik",
            ["GET ClinicVisits.Create"] = "Buka form kunjungan klinik",
            ["POST ClinicVisits.Create"] = "Simpan kunjungan klinik",
            ["GET ClinicVisits.Detail"] = "Lihat kunjungan klinik #{id}",
            ["POST ClinicVisits.Cancel"] = "Batalkan kunjungan klinik #{id}",
            ["GET ClinicVisits.Export"] = "Download Excel Kunjungan Klinik",
            ["GET MedicalRecord.Index"] = "Buka Rekam Medis",
            ["GET MedicalRecord.Employee"] = "Lihat rekam medis karyawan #{id}",
            ["GET Medicines.Index"] = "Buka Obat & Stok",
            ["POST Medicines.Save"] = "Simpan data obat",
            ["POST Medicines.Stock"] = "Ubah stok obat #{id}",
            ["POST Medicines.ToggleActive"] = "Ubah status obat #{id}",
            ["GET Medicines.Card"] = "Lihat kartu stok obat #{id}",
            ["GET Medicines.Import"] = "Buka import stok obat",
            ["GET Medicines.ImportTemplate"] = "Download template stok obat",
            ["POST Medicines.Import"] = "Cek file import stok obat",
            ["POST Medicines.ImportConfirm"] = "Simpan import stok obat dari Excel",
            ["GET Diseases.Index"] = "Buka Jenis Penyakit",
            ["POST Diseases.Save"] = "Simpan jenis penyakit",
            ["POST Diseases.ToggleActive"] = "Ubah status jenis penyakit #{id}",
            ["GET AccessLogs.Index"] = "Buka Log Akses",
            ["GET AccessLogs.Export"] = "Download Excel Log Akses",
            ["GET Employees.Detail"] = "Lihat detail karyawan #{id}",
            ["GET DiseaseCategories.Index"] = "Buka Master Kategori Penyakit",
            ["POST DiseaseCategories.Save"] = "Simpan kategori penyakit",
            ["POST DiseaseCategories.ToggleActive"] = "Ubah status kategori penyakit #{id}",
            ["POST DiseaseCategories.Delete"] = "Hapus kategori penyakit #{id}",
            ["GET MedicineUnits.Index"] = "Buka Master Satuan Obat",
            ["POST MedicineUnits.Save"] = "Simpan satuan obat",
            ["POST MedicineUnits.ToggleActive"] = "Ubah status satuan obat #{id}",
            ["POST MedicineUnits.Delete"] = "Hapus satuan obat #{id}",
            ["GET DataReset.Index"] = "Buka Reset Data",
            ["GET Checkups.FollowUps"] = "Buka Follow Up Pemkes",
            ["POST Checkups.FollowUp"] = "Ubah temuan / jadwal follow up pemeriksaan #{id}",
            ["POST Checkups.FollowUpDone"] = "Tandai follow up selesai, pemeriksaan #{id}",
            ["POST Checkups.FollowUpReopen"] = "Buka kembali follow up pemeriksaan #{id}",
            ["GET ClinicVisits.History"] = "Lihat riwayat pasien (form kunjungan klinik)",
            ["GET DrugTests.Index"] = "Buka Tes Urin & Alkohol",
            ["GET DrugTests.Create"] = "Buka form tes urin & alkohol",
            ["POST DrugTests.Create"] = "Simpan tes urin & alkohol",
            ["GET DrugTests.Detail"] = "Lihat tes urin & alkohol #{id}",
            ["POST DrugTests.Delete"] = "Hapus tes urin & alkohol #{id}",
            ["GET DrugTests.Export"] = "Download Excel Tes Urin & Alkohol",
            ["GET Attachments.Get"] = "Buka lampiran medis #{id}",
            ["POST Attachments.Upload"] = "Upload lampiran medis",
            ["POST Attachments.Delete"] = "Hapus lampiran medis #{id}",
            ["GET HealthReferences.Index"] = "Buka Batas Alkohol & Follow Up",
            ["POST HealthReferences.Save"] = "Ubah batas alkohol / usulan follow up",
            ["POST HealthReferences.Reset"] = "Reset batas alkohol / usulan follow up ke default",
            ["GET CheckupParameters.Index"] = "Buka Parameter & Skor Pemkes",
            ["GET CheckupParameters.Create"] = "Buka form tambah parameter pemkes",
            ["GET CheckupParameters.Edit"] = "Buka form ubah parameter pemkes #{id}",
            ["POST CheckupParameters.Save"] = "Simpan parameter pemkes",
            ["POST CheckupParameters.ToggleActive"] = "Ubah status parameter pemkes #{id}",
            ["POST CheckupParameters.Delete"] = "Hapus parameter pemkes #{id}",
            ["POST CheckupParameters.RiskLevels"] = "Ubah kategori risiko skor pemkes",
            ["GET UrineParameters.Index"] = "Buka Master Parameter Tes Urin",
            ["POST UrineParameters.Save"] = "Simpan parameter tes urin",
            ["POST UrineParameters.ToggleActive"] = "Ubah status parameter tes urin #{id}",
            ["POST UrineParameters.Delete"] = "Hapus parameter tes urin #{id}",
            ["GET Mitras.Index"] = "Buka Data Mitra",
            ["POST Mitras.Save"] = "Simpan data mitra",
            ["POST Mitras.ToggleActive"] = "Ubah status mitra #{id}",
            ["POST Mitras.Delete"] = "Hapus mitra #{id}",
            ["GET MitraEmployees.Index"] = "Buka Karyawan Mitra",
            ["POST MitraEmployees.Save"] = "Simpan karyawan mitra",
            ["POST MitraEmployees.ToggleActive"] = "Ubah status karyawan mitra #{id}",
            ["POST MitraEmployees.Delete"] = "Hapus karyawan mitra #{id}",
            ["GET FtwSettings.Index"] = "Buka Pengaturan Pengisian FTW",
            ["POST FtwSettings.Save"] = "Ubah pengaturan pengisian FTW (jeda / jam reset / isi ulang)",
            ["POST FtwSettings.Reset"] = "Reset pengaturan pengisian FTW ke default",
            ["GET SiteLocations.Index"] = "Buka Lokasi Site",
            ["GET SiteLocations.Create"] = "Buka form tambah lokasi site",
            ["GET SiteLocations.Edit"] = "Buka form ubah lokasi site #{id}",
            ["POST SiteLocations.Save"] = "Simpan lokasi site (kunci lokasi)",
            ["POST SiteLocations.ToggleActive"] = "Ubah status lokasi site #{id}",
            ["POST SiteLocations.Delete"] = "Hapus lokasi site #{id}"
        };

        private readonly RequestDelegate _next;

        public AccessLogMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IAccessLogger accessLogger)
        {
            try
            {
                await _next(context);
            }
            catch
            {
                // Error tetap dicatat (status 500) lalu diteruskan ke exception handler
                await WriteAsync(context, accessLogger, StatusCodes.Status500InternalServerError);
                throw;
            }

            await WriteAsync(context, accessLogger, context.Response.StatusCode);
        }

        private static async Task WriteAsync(HttpContext context, IAccessLogger accessLogger, int statusCode)
        {

            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null || !context.User.IsInRole("Admin"))
                return;

            var controller = action.ControllerName;
            if (controller == "Account")
                return;   // login/logout/akses ditolak dicatat manual di AccountController

            var key = $"{controller}.{action.ActionName}";

            // Action yang menolak sendiri (Forbid()/redirect ke AccessDenied), mis. reset password akun lain
            var location = context.Response.Headers.Location.ToString();
            if (statusCode is 301 or 302 && location.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
            {
                await accessLogger.LogAsync(AccessLogEvents.AccessDenied,
                    "Akses ditolak: " + context.Request.Path + context.Request.QueryString, StatusCodes.Status403Forbidden);
                return;
            }

            if (SkipActions.Contains(key))
                return;

            var method = context.Request.Method.ToUpperInvariant();
            var isChange = method is "POST" or "PUT" or "PATCH" or "DELETE";
            var isExport = action.ActionName.Contains("Export", StringComparison.OrdinalIgnoreCase)
                           || action.ActionName.Contains("Template", StringComparison.OrdinalIgnoreCase);
            var eventType = isChange ? AccessLogEvents.Change : isExport ? AccessLogEvents.Export : AccessLogEvents.View;

            var id = context.Request.RouteValues.TryGetValue("id", out var idValue) ? idValue?.ToString() : null;
            var activity = Labels.TryGetValue($"{method} {key}", out var label)
                ? label.Replace("{id}", id ?? "?")
                : $"{(isChange ? "Ubah" : "Buka")} {controller}/{action.ActionName}{(id is null ? "" : " #" + id)}";

            await accessLogger.LogAsync(eventType, activity, statusCode);
        }
    }

    /// <summary>
    /// Mencatat penolakan dari [HasPermission]/[Authorize] (admin membuka menu di luar hak aksesnya),
    /// lalu meneruskan ke perilaku bawaan (redirect ke halaman Akses Ditolak).
    /// </summary>
    public class AccessLogAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && context.User.IsInRole("Admin"))
            {
                var logger = context.RequestServices.GetRequiredService<IAccessLogger>();
                await logger.LogAsync(AccessLogEvents.AccessDenied,
                    "Akses ditolak: " + context.Request.Path + context.Request.QueryString, StatusCodes.Status403Forbidden);
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }

    /// <summary>Hapus log lama sekali sehari. Masa simpan: App:AccessLogRetentionDays (default 365 hari).</summary>
    public class AccessLogCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AccessLogCleanupService> _logger;
        private readonly int _retentionDays;

        public AccessLogCleanupService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
            ILogger<AccessLogCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _retentionDays = Math.Max(configuration.GetValue("App:AccessLogRetentionDays", 365), 30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Tunggu sebentar setelah startup supaya tidak berebut dengan request pertama
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IAccessLogRepository>();
                    var deleted = await repo.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-_retentionDays));
                    if (deleted > 0)
                        _logger.LogInformation("Access log: {Count} baris lebih dari {Days} hari dihapus", deleted, _retentionDays);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal membersihkan access log lama");
                }

                try { await Task.Delay(TimeSpan.FromDays(1), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
