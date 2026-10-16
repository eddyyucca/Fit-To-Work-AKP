using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MasterOrganization)]
    [Route("Admin/Organization")]
    public class OrganizationController : Controller
    {
        private readonly IOrganizationRepository _orgRepository;

        public OrganizationController(IOrganizationRepository orgRepository)
        {
            _orgRepository = orgRepository;
        }

        // Tab default: division, department, section, atau position
        [HttpGet("")]
        public async Task<IActionResult> Index(string tab = "division")
        {
            ViewBag.ActiveTab = tab is "department" or "section" or "position" ? tab : "division";

            ViewBag.Divisions = (await _orgRepository.GetDivisionsAsync()).ToList();
            ViewBag.Departments = (await _orgRepository.GetDepartmentsAsync()).ToList();
            ViewBag.Sections = (await _orgRepository.GetSectionsAsync()).ToList();
            ViewBag.Positions = (await _orgRepository.GetPositionsAsync()).ToList();
            ViewBag.DepartmentOptions = (await _orgRepository.GetDepartmentsAsync(onlyActive: true)).ToList();

            return View();
        }

        // Tambah & Edit sekarang lewat modal di halaman Index, jadi tidak perlu
        // halaman GET terpisah. Kalau validasi gagal, redirect balik ke Index
        // dengan pesan error di TempData.

        // ==================== Division ====================

        [HttpPost("Division/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDivision(DivisionFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menambah division: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "division" });
            }

            await _orgRepository.CreateDivisionAsync(new Division
            {
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Division berhasil ditambahkan.";
            return RedirectToAction(nameof(Index), new { tab = "division" });
        }

        [HttpPost("Division/Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDivision(int id, DivisionFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menyimpan perubahan division: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "division" });
            }

            await _orgRepository.UpdateDivisionAsync(new Division
            {
                Id = id,
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Division berhasil diperbarui.";
            return RedirectToAction(nameof(Index), new { tab = "division" });
        }

        [HttpPost("Division/ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDivisionActive(int id, bool activate)
        {
            await _orgRepository.SetDivisionActiveAsync(id, activate);
            TempData["Success"] = activate ? "Division diaktifkan." : "Division dinonaktifkan.";
            return RedirectToAction(nameof(Index), new { tab = "division" });
        }

        [HttpPost("Division/Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDivision(int id)
        {
            try
            {
                await _orgRepository.DeleteDivisionAsync(id);
                TempData["Success"] = "Division berhasil dihapus.";
            }
            catch (Exception)
            {
                TempData["Error"] = "Division tidak bisa dihapus karena masih dipakai oleh data karyawan. Nonaktifkan saja.";
            }
            return RedirectToAction(nameof(Index), new { tab = "division" });
        }

        // ==================== Department ====================

        [HttpPost("Department/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDepartment(DepartmentFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menambah department: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "department" });
            }

            await _orgRepository.CreateDepartmentAsync(new Department
            {
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Department berhasil ditambahkan.";
            return RedirectToAction(nameof(Index), new { tab = "department" });
        }

        [HttpPost("Department/Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDepartment(int id, DepartmentFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menyimpan perubahan department: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "department" });
            }

            await _orgRepository.UpdateDepartmentAsync(new Department
            {
                Id = id,
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Department berhasil diperbarui.";
            return RedirectToAction(nameof(Index), new { tab = "department" });
        }

        [HttpPost("Department/ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDepartmentActive(int id, bool activate)
        {
            await _orgRepository.SetDepartmentActiveAsync(id, activate);
            TempData["Success"] = activate ? "Department diaktifkan." : "Department dinonaktifkan.";
            return RedirectToAction(nameof(Index), new { tab = "department" });
        }

        [HttpPost("Department/Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            try
            {
                await _orgRepository.DeleteDepartmentAsync(id);
                TempData["Success"] = "Department berhasil dihapus.";
            }
            catch (Exception)
            {
                TempData["Error"] = "Department tidak bisa dihapus karena masih dipakai oleh Section. Nonaktifkan saja, atau pindahkan/hapus Section-nya dulu.";
            }
            return RedirectToAction(nameof(Index), new { tab = "department" });
        }

        // ==================== Section ====================

        [HttpPost("Section/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSection(SectionFormViewModel model)
        {
            if (model.DepartmentId <= 0)
                ModelState.AddModelError(nameof(model.DepartmentId), "Department wajib dipilih");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menambah section: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "section" });
            }

            await _orgRepository.CreateSectionAsync(new Section
            {
                DepartmentId = model.DepartmentId,
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Section berhasil ditambahkan.";
            return RedirectToAction(nameof(Index), new { tab = "section" });
        }

        [HttpPost("Section/Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSection(int id, SectionFormViewModel model)
        {
            if (model.DepartmentId <= 0)
                ModelState.AddModelError(nameof(model.DepartmentId), "Department wajib dipilih");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menyimpan perubahan section: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "section" });
            }

            await _orgRepository.UpdateSectionAsync(new Section
            {
                Id = id,
                DepartmentId = model.DepartmentId,
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Section berhasil diperbarui.";
            return RedirectToAction(nameof(Index), new { tab = "section" });
        }

        [HttpPost("Section/ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSectionActive(int id, bool activate)
        {
            await _orgRepository.SetSectionActiveAsync(id, activate);
            TempData["Success"] = activate ? "Section diaktifkan." : "Section dinonaktifkan.";
            return RedirectToAction(nameof(Index), new { tab = "section" });
        }

        [HttpPost("Section/Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSection(int id)
        {
            await _orgRepository.DeleteSectionAsync(id);
            TempData["Success"] = "Section berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { tab = "section" });
        }

        // ==================== Position ====================

        [HttpPost("Position/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePosition(PositionFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menambah position: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "position" });
            }

            await _orgRepository.CreatePositionAsync(new Position
            {
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Position berhasil ditambahkan.";
            return RedirectToAction(nameof(Index), new { tab = "position" });
        }

        [HttpPost("Position/Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPosition(int id, PositionFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menyimpan perubahan position: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { tab = "position" });
            }

            await _orgRepository.UpdatePositionAsync(new Position
            {
                Id = id,
                Name = model.Name.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Position berhasil diperbarui.";
            return RedirectToAction(nameof(Index), new { tab = "position" });
        }

        [HttpPost("Position/ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePositionActive(int id, bool activate)
        {
            await _orgRepository.SetPositionActiveAsync(id, activate);
            TempData["Success"] = activate ? "Position diaktifkan." : "Position dinonaktifkan.";
            return RedirectToAction(nameof(Index), new { tab = "position" });
        }

        [HttpPost("Position/Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePosition(int id)
        {
            await _orgRepository.DeletePositionAsync(id);
            TempData["Success"] = "Position berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { tab = "position" });
        }
    }
}