using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Lihat / download / upload / hapus lampiran hasil pemeriksaan.
    /// Hak akses mengikuti data induknya: pemkes = medical.checkup, tes urin & alkohol = medical.drugtest.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalCheckup, Permissions.MedicalDrugTest)]
    [Route("Admin/Attachments")]
    public class AttachmentsController : Controller
    {
        private readonly IMedicalAttachmentRepository _repo;
        private readonly IMedicalFileStorage _storage;
        private readonly IPermissionService _permissionService;
        private readonly IHealthCheckupRepository _checkupRepository;
        private readonly IDrugAlcoholTestRepository _drugTestRepository;

        public AttachmentsController(IMedicalAttachmentRepository repo, IMedicalFileStorage storage, IPermissionService permissionService,
            IHealthCheckupRepository checkupRepository, IDrugAlcoholTestRepository drugTestRepository)
        {
            _repo = repo;
            _storage = storage;
            _permissionService = permissionService;
            _checkupRepository = checkupRepository;
            _drugTestRepository = drugTestRepository;
        }

        private async Task<bool> CanAccessAsync(string entityType)
        {
            var access = await _permissionService.GetAsync(User);
            return entityType switch
            {
                AttachmentEntityTypes.Checkup => access.Has(Permissions.MedicalCheckup),
                AttachmentEntityTypes.DrugTest => access.Has(Permissions.MedicalDrugTest),
                _ => false
            };
        }

        private async Task<bool> EntityExistsAsync(string entityType, int entityId) => entityType switch
        {
            AttachmentEntityTypes.Checkup => await _checkupRepository.GetByIdAsync(entityId) is not null,
            AttachmentEntityTypes.DrugTest => await _drugTestRepository.GetByIdAsync(entityId) is not null,
            _ => false
        };

        private IActionResult BackToEntity(string entityType, int entityId) => entityType == AttachmentEntityTypes.DrugTest
            ? RedirectToAction("Detail", "DrugTests", new { id = entityId })
            : RedirectToAction("Detail", "Checkups", new { id = entityId });

        // GET Admin/Attachments/5            -> tampil di browser (PDF/foto)
        // GET Admin/Attachments/5?download=1 -> download
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, bool download = false)
        {
            var a = await _repo.GetAsync(id);
            if (a is null)
                return NotFound();
            if (!await CanAccessAsync(a.EntityType))
                return Forbid();
            return Serve(this, _storage, a, download);
        }

        [HttpPost("Upload")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(110L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024 * 1024)]
        public async Task<IActionResult> Upload(string entityType, int entityId, List<IFormFile>? files)
        {
            if (!await CanAccessAsync(entityType))
                return Forbid();
            if (!await EntityExistsAsync(entityType, entityId))
                return NotFound();

            if (files is null || files.All(f => f.Length == 0))
            {
                TempData["Error"] = "Pilih file yang mau diupload.";
                return BackToEntity(entityType, entityId);
            }

            var errors = _repo.Validate(files);
            if (errors.Count > 0)
            {
                TempData["Error"] = "Upload dibatalkan. " + string.Join(" ", errors);
                return BackToEntity(entityType, entityId);
            }

            var n = await _repo.SaveAsync(entityType, entityId, files, User.AdminName());
            TempData["Success"] = $"{n} file berhasil diupload.";
            return BackToEntity(entityType, entityId);
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _repo.GetAsync(id);
            if (a is null)
                return NotFound();
            if (!await CanAccessAsync(a.EntityType))
                return Forbid();

            await _repo.DeleteAsync(a);
            TempData["Success"] = $"Lampiran \"{a.FileName}\" dihapus.";
            return BackToEntity(a.EntityType, a.EntityId);
        }

        /// <summary>Kirim file: inline untuk dilihat di browser, atau sebagai download. Dipakai juga oleh Portal.</summary>
        internal static IActionResult Serve(Controller c, IMedicalFileStorage storage, MedicalAttachment a, bool download)
        {
            var stream = storage.OpenRead(a.StoredPath);
            if (stream is null)
                return c.NotFound("File lampiran tidak ditemukan di server.");

            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers.CacheControl = "private, no-store";   // data medis: jangan disimpan di cache bersama
            if (download)
                return c.File(stream, a.ContentType, a.FileName);

            var cd = new ContentDispositionHeaderValue("inline");
            cd.SetHttpFileName(a.FileName);
            c.Response.Headers.ContentDisposition = cd.ToString();
            return c.File(stream, a.ContentType);
        }
    }
}
