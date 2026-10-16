using System.ComponentModel.DataAnnotations;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class RoleFormViewModel
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Nama role wajib diisi")]
        [StringLength(100)]
        [Display(Name = "Nama Role")]
        public string Name { get; set; } = string.Empty;

        [StringLength(250)]
        [Display(Name = "Keterangan")]
        public string? Description { get; set; }

        public List<string> Permissions { get; set; } = new();

        // Hanya untuk tampilan
        public bool IsSuperAdmin { get; set; }
        public int UserCount { get; set; }
    }

    /// <summary>
    /// Kelola role & hak akses per menu (butuh permission admin.users).
    /// Admin non-Super Admin hanya boleh memberi hak akses yang ia miliki sendiri,
    /// dan tidak bisa mengubah role Super Admin.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.AdminUsers)]
    [Route("Admin/Roles")]
    public class RolesController : Controller
    {
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionService _permissionService;

        public RolesController(IRoleRepository roleRepository, IPermissionService permissionService)
        {
            _roleRepository = roleRepository;
            _permissionService = permissionService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewBag.Access = await _permissionService.GetAsync(User);
            return View(await _roleRepository.GetAllAsync());
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Access = await _permissionService.GetAsync(User);
            return View("Edit", new RoleFormViewModel());
        }

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var role = await _roleRepository.GetByIdAsync(id);
            if (role is null)
                return NotFound();

            var access = await _permissionService.GetAsync(User);
            if (!CanEdit(access, role))
            {
                TempData["Error"] = "Anda tidak berhak mengubah role ini.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Access = access;
            return View(new RoleFormViewModel
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                Permissions = role.Permissions,
                IsSuperAdmin = role.IsSuperAdmin,
                UserCount = role.UserCount
            });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(RoleFormViewModel model)
        {
            var access = await _permissionService.GetAsync(User);
            model.Name = model.Name?.Trim() ?? string.Empty;
            model.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();

            // Buang key yang tidak dikenal (mis. request dimanipulasi)
            var requested = (model.Permissions ?? new List<string>()).Where(Permissions.AllKeys.Contains).Distinct().ToList();

            Role? existing = null;
            if (model.Id.HasValue)
            {
                existing = await _roleRepository.GetByIdAsync(model.Id.Value);
                if (existing is null)
                    return NotFound();
                if (!CanEdit(access, existing))
                {
                    TempData["Error"] = "Anda tidak berhak mengubah role ini.";
                    return RedirectToAction(nameof(Index));
                }
                model.IsSuperAdmin = existing.IsSuperAdmin;
                model.UserCount = existing.UserCount;
            }

            if (!string.IsNullOrEmpty(model.Name) && await _roleRepository.NameExistsAsync(model.Name, model.Id))
                ModelState.AddModelError(nameof(model.Name), "Nama role sudah dipakai");

            if (!model.IsSuperAdmin)
            {
                if (requested.Count == 0)
                    ModelState.AddModelError(nameof(model.Permissions), "Pilih minimal satu hak akses");

                var notOwned = requested.Where(p => !access.Has(p)).ToList();
                if (notOwned.Count > 0)
                    ModelState.AddModelError(nameof(model.Permissions), "Anda hanya boleh memberi hak akses yang Anda miliki sendiri");
            }

            if (!ModelState.IsValid)
            {
                model.Permissions = requested;
                ViewBag.Access = access;
                return View("Edit", model);
            }

            var role = new Role
            {
                Id = model.Id ?? 0,
                Name = model.Name,
                Description = model.Description,
                IsSuperAdmin = model.IsSuperAdmin
            };

            if (existing is null)
                await _roleRepository.CreateAsync(role, requested);
            else
                await _roleRepository.UpdateAsync(role, requested);

            _permissionService.InvalidateAll();
            TempData["Success"] = $"Role '{role.Name}' berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var role = await _roleRepository.GetByIdAsync(id);
            if (role is null)
                return NotFound();

            var access = await _permissionService.GetAsync(User);
            if (role.IsSuperAdmin)
                TempData["Error"] = "Role Super Admin tidak bisa dihapus.";
            else if (!CanEdit(access, role))
                TempData["Error"] = "Anda tidak berhak menghapus role ini.";
            else if (role.UserCount > 0)
                TempData["Error"] = $"Role '{role.Name}' masih dipakai {role.UserCount} user. Pindahkan user ke role lain dulu.";
            else
            {
                await _roleRepository.DeleteAsync(id);
                _permissionService.InvalidateAll();
                TempData["Success"] = $"Role '{role.Name}' berhasil dihapus.";
            }

            return RedirectToAction(nameof(Index));
        }

        // Super Admin boleh ubah semua role. Selain itu: bukan role Super Admin,
        // dan semua hak akses role tersebut juga dimiliki si pengubah (supaya tidak bisa "naik pangkat").
        private static bool CanEdit(AdminAccess access, Role role) =>
            access.IsSuperAdmin || (!role.IsSuperAdmin && role.Permissions.All(access.Has));
    }
}
