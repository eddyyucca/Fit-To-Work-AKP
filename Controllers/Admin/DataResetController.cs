using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Pengaturan &gt; Reset Data: mengosongkan data per modul (mis. setelah uji coba sebelum go-live).
    /// Hanya Super Admin. Wajib ketik RESET + password akun sendiri. Tidak bisa dibatalkan.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/DataReset")]
    public class DataResetController : Controller
    {
        public const string ConfirmationText = "RESET";

        private readonly IDataResetRepository _repo;
        private readonly IPermissionService _permissionService;
        private readonly IAdminUserRepository _adminUserRepository;
        private readonly IAccessLogger _accessLogger;
        private readonly IDashboardNotifier _notifier;
        private readonly ILogger<DataResetController> _logger;
        private readonly IMedicalAttachmentRepository _attachmentRepository;

        public DataResetController(IDataResetRepository repo, IPermissionService permissionService,
            IAdminUserRepository adminUserRepository, IAccessLogger accessLogger, IDashboardNotifier notifier,
            ILogger<DataResetController> logger, IMedicalAttachmentRepository attachmentRepository)
        {
            _attachmentRepository = attachmentRepository;
            _repo = repo;
            _permissionService = permissionService;
            _adminUserRepository = adminUserRepository;
            _accessLogger = accessLogger;
            _notifier = notifier;
            _logger = logger;
        }

        private async Task<bool> IsSuperAdminAsync() => (await _permissionService.GetAsync(User)).IsSuperAdmin;

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            if (!await IsSuperAdminAsync())
                return Forbid();

            ViewBag.Counts = await _repo.CountAsync();
            return View();
        }

        [HttpPost("Execute")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Execute(string[]? modules, string? confirmText, string? password)
        {
            if (!await IsSuperAdminAsync())
                return Forbid();

            var selected = ResetModules.WithDependencies(modules ?? Array.Empty<string>());
            if (selected.Count == 0)
            {
                TempData["Error"] = "Pilih minimal satu modul yang mau direset.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.Equals(confirmText?.Trim(), ConfirmationText, StringComparison.Ordinal))
            {
                TempData["Error"] = $"Konfirmasi tidak sesuai. Ketik persis \"{ConfirmationText}\" (huruf besar). Tidak ada data yang dihapus.";
                return RedirectToAction(nameof(Index));
            }

            var admin = await _adminUserRepository.GetByIdAsync(User.AdminId() ?? 0);
            if (admin is null || string.IsNullOrEmpty(password) || !PasswordHasher.Verify(password, admin.PasswordHash))
            {
                await _accessLogger.LogAsync(AccessLogEvents.Reset, "Reset data GAGAL: password salah", StatusCodes.Status400BadRequest);
                TempData["Error"] = "Password salah. Tidak ada data yang dihapus.";
                return RedirectToAction(nameof(Index));
            }

            Dictionary<string, int> deleted;
            try
            {
                deleted = await _repo.ResetAsync(selected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reset data gagal untuk modul {Modules}", string.Join(",", selected));
                TempData["Error"] = "Reset gagal, semua perubahan dibatalkan (tidak ada data yang terhapus). " + ex.Message;
                return RedirectToAction(nameof(Index));
            }

            // File lampiran yang catatannya ikut terhapus dibersihkan dari disk
            if (selected.Contains(ResetModules.Checkup) || selected.Contains(ResetModules.DrugTest))
            {
                try
                {
                    await _attachmentRepository.CleanupOrphanFilesAsync(TimeSpan.FromMinutes(1));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pembersihan file lampiran setelah reset gagal");
                }
            }

            // Ringkasan: "Riwayat Fit To Work (120), Riwayat Daftar Gym (45)"
            var summary = string.Join(", ", ResetModules.All
                .Where(m => selected.Contains(m.Key))
                .Select(m => $"{m.Label} ({(deleted.TryGetValue(m.Key, out var n) ? n : 0):N0})"));

            // Dicatat SETELAH hapus, jadi tetap ada walau Log Akses ikut direset
            await _accessLogger.LogAsync(AccessLogEvents.Reset, Truncate("Reset data: " + summary, 300), StatusCodes.Status200OK);
            await _notifier.NotifyRefreshAllAsync();

            TempData["Success"] = "Reset selesai. Data yang dihapus: " + summary + ".";
            return RedirectToAction(nameof(Index));
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
