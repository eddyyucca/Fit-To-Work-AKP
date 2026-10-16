using System.Security.Claims;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Kelola akun admin (butuh permission admin.users).
    /// Ganti password SENDIRI boleh untuk semua admin, apa pun role-nya.
    /// Aturan keamanan:
    ///  - hanya Super Admin yang boleh membuat/mengubah/menghapus akun Super Admin atau memberi role Super Admin;
    ///  - tidak bisa mengubah role sendiri atau menghapus akun sendiri;
    ///  - Super Admin aktif terakhir tidak bisa dihapus / diturunkan / dinonaktifkan;
    ///  - tidak bisa menonaktifkan akun sendiri.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Users")]
    public class UsersController : Controller
    {
        private readonly IAdminUserRepository _adminUserRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionService _permissionService;

        public UsersController(
            IAdminUserRepository adminUserRepository,
            IEmployeeRepository employeeRepository,
            IRoleRepository roleRepository,
            IPermissionService permissionService)
        {
            _adminUserRepository = adminUserRepository;
            _employeeRepository = employeeRepository;
            _roleRepository = roleRepository;
            _permissionService = permissionService;
        }

        private int CurrentAdminId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Role yang boleh diberikan oleh admin yang sedang login (non-Super Admin tidak bisa memberi role Super Admin)
        private async Task<List<Role>> GetAssignableRolesAsync(AdminAccess access) =>
            (await _roleRepository.GetAllAsync()).Where(r => access.IsSuperAdmin || !r.IsSuperAdmin).ToList();

        // Dipakai oleh kotak autocomplete di halaman Tambah User Admin,
        // supaya Nama Lengkap bisa diisi otomatis dari data karyawan (hindari typo).
        [HttpGet("SearchEmployee")]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> SearchEmployee(string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(Array.Empty<object>());

            var employees = await _employeeRepository.GetAllAsync(q.Trim());
            var result = employees.Take(10).Select(e => new { id = e.Id, nip = e.NIP, nama = e.Nama, section = e.SectionName });
            return Json(result);
        }

        [HttpGet("")]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Index()
        {
            var access = await _permissionService.GetAsync(User);
            var users = await _adminUserRepository.GetAllAsync();
            ViewBag.CurrentAdminId = CurrentAdminId;
            ViewBag.CurrentIsSuperAdmin = access.IsSuperAdmin;
            ViewBag.Roles = await GetAssignableRolesAsync(access);
            return View(users);
        }

        [HttpGet("Create")]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Create()
        {
            ViewBag.Roles = await GetAssignableRolesAsync(await _permissionService.GetAsync(User));
            return View(new AdminUserCreateViewModel());
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Create(AdminUserCreateViewModel model)
        {
            var access = await _permissionService.GetAsync(User);
            var assignable = await GetAssignableRolesAsync(access);

            if (await _adminUserRepository.UsernameExistsAsync(model.Username.Trim()))
                ModelState.AddModelError(nameof(model.Username), "Username sudah dipakai");

            if (model.RoleId.HasValue && assignable.All(r => r.Id != model.RoleId))
                ModelState.AddModelError(nameof(model.RoleId), "Role tidak valid atau Anda tidak berhak memberikannya");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = assignable;
                return View(model);
            }

            var admin = new AdminUser
            {
                Username = model.Username.Trim(),
                FullName = model.FullName.Trim(),
                PasswordHash = PasswordHasher.Hash(model.Password),
                RoleId = model.RoleId
            };

            await _adminUserRepository.CreateAsync(admin);
            TempData["Success"] = $"Akun admin '{admin.Username}' berhasil dibuat.";
            return RedirectToAction(nameof(Index));
        }

        // POST Admin/Users/SetRole/5  (roleId dari dropdown di tabel user)
        [HttpPost("SetRole/{id:int}")]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> SetRole(int id, int roleId)
        {
            if (id == CurrentAdminId)
            {
                TempData["Error"] = "Anda tidak bisa mengubah role akun Anda sendiri.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _adminUserRepository.GetByIdAsync(id);
            var newRole = await _roleRepository.GetByIdAsync(roleId);
            if (user is null || newRole is null)
                return NotFound();

            var access = await _permissionService.GetAsync(User);
            if (!access.IsSuperAdmin && (user.IsSuperAdmin || newRole.IsSuperAdmin))
            {
                TempData["Error"] = "Hanya Super Admin yang boleh mengatur akun/role Super Admin.";
                return RedirectToAction(nameof(Index));
            }

            if (user.IsSuperAdmin && user.IsActive && !newRole.IsSuperAdmin && await _adminUserRepository.CountSuperAdminsAsync() <= 1)
            {
                TempData["Error"] = "Tidak bisa menurunkan Super Admin terakhir. Jadikan akun lain Super Admin dulu.";
                return RedirectToAction(nameof(Index));
            }

            await _adminUserRepository.UpdateRoleAsync(id, roleId);
            _permissionService.InvalidateAll();
            TempData["Success"] = $"Role '{user.Username}' diubah menjadi {newRole.Name}.";
            return RedirectToAction(nameof(Index));
        }

        // Admin dengan hak "User Admin & Role" bisa reset password admin lain tanpa password lama.
        // Kalau admin mengganti passwordnya sendiri, password saat ini WAJIB diverifikasi dulu.
        [HttpGet("ChangePassword/{id:int}")]
        public async Task<IActionResult> ChangePassword(int id)
        {
            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();

            if (await CannotManageAsync(user))
                return Forbid();

            var model = new AdminChangePasswordViewModel
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                IsSelf = user.Id == CurrentAdminId
            };

            return View(model);
        }

        [HttpPost("ChangePassword/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(int id, AdminChangePasswordViewModel model)
        {
            model.Id = id;
            model.IsSelf = id == CurrentAdminId;

            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();

            if (await CannotManageAsync(user))
                return Forbid();

            model.Username = user.Username;
            model.FullName = user.FullName;

            if (model.IsSelf)
            {
                if (string.IsNullOrWhiteSpace(model.CurrentPassword))
                {
                    ModelState.AddModelError(nameof(model.CurrentPassword), "Masukkan password Anda saat ini");
                }
                else if (!PasswordHasher.Verify(model.CurrentPassword, user.PasswordHash))
                {
                    ModelState.AddModelError(nameof(model.CurrentPassword), "Password saat ini salah");
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            await _adminUserRepository.UpdatePasswordAsync(id, PasswordHasher.Hash(model.NewPassword));
            TempData["Success"] = model.IsSelf
                ? "Password Anda berhasil diganti."
                : $"Password untuk '{user.Username}' berhasil direset.";

            // Admin tanpa hak kelola user (mis. role HSE) dikembalikan ke halaman awalnya, bukan ke daftar user
            var access = await _permissionService.GetAsync(User);
            return access.Has(Permissions.AdminUsers)
                ? RedirectToAction(nameof(Index))
                : RedirectToAction("Home", "Admin");
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Delete(int id)
        {
            if (id == CurrentAdminId)
            {
                TempData["Error"] = "Anda tidak bisa menghapus akun Anda sendiri.";
                return RedirectToAction(nameof(Index));
            }

            if (await _adminUserRepository.CountAsync() <= 1)
            {
                TempData["Error"] = "Tidak bisa menghapus admin terakhir yang tersisa.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();

            if (await CannotManageAsync(user))
            {
                TempData["Error"] = "Hanya Super Admin yang boleh menghapus akun Super Admin.";
                return RedirectToAction(nameof(Index));
            }

            if (user.IsSuperAdmin && user.IsActive && await _adminUserRepository.CountSuperAdminsAsync() <= 1)
            {
                TempData["Error"] = "Tidak bisa menghapus Super Admin terakhir.";
                return RedirectToAction(nameof(Index));
            }

            await _adminUserRepository.DeleteAsync(id);
            _permissionService.InvalidateAll();
            TempData["Success"] = $"Akun admin '{user.Username}' berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        // GET Admin/Users/Edit/5 : ubah nama lengkap & tautan karyawan
        [HttpGet("Edit/{id:int}")]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();
            if (await CannotManageAsync(user))
                return Forbid();

            return View(ToEditModel(user));
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> Edit(int id, AdminUserEditViewModel model)
        {
            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();
            if (await CannotManageAsync(user))
                return Forbid();

            model.FullName = (model.FullName ?? string.Empty).Trim();

            Employee? employee = null;
            if (model.EmployeeId.HasValue)
            {
                employee = await _employeeRepository.GetByIdAsync(model.EmployeeId.Value);
                if (employee is null)
                    ModelState.AddModelError(nameof(model.EmployeeId), "Karyawan tidak ditemukan.");
                else if (employee.IsMitra)
                    ModelState.AddModelError(nameof(model.EmployeeId), "Akun admin hanya bisa dihubungkan ke karyawan AKP, bukan karyawan mitra.");
                else if (await _adminUserRepository.GetUsernameLinkedToEmployeeAsync(employee.Id, id) is { } otherUsername)
                    ModelState.AddModelError(nameof(model.EmployeeId), $"Karyawan {employee.NIP} - {employee.Nama} sudah terhubung dengan akun '{otherUsername}'.");
            }

            if (!ModelState.IsValid)
            {
                // Field tampilan diisi ulang dari database, bukan dari form
                var vm = ToEditModel(user);
                vm.FullName = model.FullName;
                vm.EmployeeId = model.EmployeeId;
                vm.EmployeeLabel = employee is null ? null : $"{employee.NIP} - {employee.Nama}";
                return View(vm);
            }

            await _adminUserRepository.UpdateProfileAsync(id, model.FullName, model.EmployeeId);

            // Edit akun sendiri: nama di cookie (dipakai sebagai nama pemeriksa/petugas) ikut diperbarui
            if (id == CurrentAdminId && model.FullName != user.FullName)
                await RefreshOwnNameClaimAsync(model.FullName);

            TempData["Success"] = $"Data user '{user.Username}' berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        // POST Admin/Users/ToggleActive/5?activate=false
        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        [HasPermission(Permissions.AdminUsers)]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            if (id == CurrentAdminId)
            {
                TempData["Error"] = "Anda tidak bisa menonaktifkan akun Anda sendiri.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _adminUserRepository.GetByIdAsync(id);
            if (user is null)
                return NotFound();

            if (await CannotManageAsync(user))
            {
                TempData["Error"] = "Hanya Super Admin yang boleh mengubah status akun Super Admin.";
                return RedirectToAction(nameof(Index));
            }

            if (!activate && user.IsSuperAdmin && user.IsActive && await _adminUserRepository.CountSuperAdminsAsync() <= 1)
            {
                TempData["Error"] = "Tidak bisa menonaktifkan Super Admin aktif terakhir.";
                return RedirectToAction(nameof(Index));
            }

            await _adminUserRepository.SetActiveAsync(id, activate);
            _permissionService.InvalidateAll();   // sesi akun yang dinonaktifkan langsung diputus di request berikutnya
            TempData["Success"] = activate
                ? $"Akun '{user.Username}' diaktifkan kembali."
                : $"Akun '{user.Username}' dinonaktifkan. Akun ini tidak bisa login dan sesinya yang aktif diputus.";
            return RedirectToAction(nameof(Index));
        }

        private AdminUserEditViewModel ToEditModel(AdminUser user) => new()
        {
            Id = user.Id,
            Username = user.Username,
            RoleName = user.RoleName,
            IsActive = user.IsActive,
            IsSelf = user.Id == CurrentAdminId,
            FullName = user.FullName,
            EmployeeId = user.EmployeeId,
            EmployeeLabel = user.EmployeeId.HasValue ? $"{user.EmployeeNip} - {user.EmployeeNama}" : null
        };

        // Terbitkan ulang cookie login dengan nama baru, masa berlaku sesi tetap sama
        private async Task RefreshOwnNameClaimAsync(string fullName)
        {
            var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!auth.Succeeded || auth.Principal?.Identity is not ClaimsIdentity identity)
                return;

            var old = identity.FindFirst(ClaimTypes.Name);
            if (old is not null)
                identity.RemoveClaim(old);
            identity.AddClaim(new Claim(ClaimTypes.Name, fullName));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, auth.Principal, auth.Properties);
        }

        // true = admin yang sedang login TIDAK boleh mengelola akun target.
        // Akun sendiri selalu boleh (ganti password). Akun lain butuh admin.users,
        // dan akun Super Admin hanya boleh dikelola sesama Super Admin.
        private async Task<bool> CannotManageAsync(AdminUser target)
        {
            if (target.Id == CurrentAdminId)
                return false;

            var access = await _permissionService.GetAsync(User);
            if (!access.Has(Permissions.AdminUsers))
                return true;
            return target.IsSuperAdmin && !access.IsSuperAdmin;
        }
    }
}
