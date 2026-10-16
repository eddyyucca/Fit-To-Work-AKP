using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MasterFormOptions)]
    [Route("Admin/FormOptions")]
    public class FormOptionsController : Controller
    {
        private readonly IFormOptionRepository _formOptionRepository;

        public FormOptionsController(IFormOptionRepository formOptionRepository)
        {
            _formOptionRepository = formOptionRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string? group)
        {
            var activeGroup = string.IsNullOrWhiteSpace(group) || !FormOptionGroups.All.Contains(group)
                ? FormOptionGroups.All.First()
                : group;

            var options = await _formOptionRepository.GetAllByGroupAsync(activeGroup);

            ViewBag.ActiveGroup = activeGroup;
            ViewBag.Groups = FormOptionGroups.All;
            ViewBag.GroupLabels = FormOptionGroups.Labels;

            return View(options);
        }

        // Tambah & Edit sekarang lewat modal di halaman Index, jadi tidak perlu
        // halaman GET terpisah lagi. Kalau validasi gagal, redirect balik ke
        // Index dengan pesan error (modal tidak auto-terbuka lagi - trade-off
        // yang wajar mengingat field-nya sederhana & sudah divalidasi HTML5 juga).

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FormOptionFormViewModel model)
        {
            if (!FormOptionGroups.All.Contains(model.GroupKey))
                ModelState.AddModelError(string.Empty, "Group pilihan tidak valid.");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menambah pilihan: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { group = model.GroupKey });
            }

            await _formOptionRepository.CreateAsync(new FormOption
            {
                GroupKey = model.GroupKey,
                OptionValue = model.OptionValue.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = "Pilihan baru berhasil ditambahkan.";
            return RedirectToAction(nameof(Index), new { group = model.GroupKey });
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FormOptionFormViewModel model)
        {
            var option = await _formOptionRepository.GetByIdAsync(id);
            if (option is null) return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Gagal menyimpan perubahan: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { group = option.GroupKey });
            }

            option.OptionValue = model.OptionValue.Trim();
            option.SortOrder = model.SortOrder;
            option.IsActive = model.IsActive;

            await _formOptionRepository.UpdateAsync(option);

            TempData["Success"] = "Pilihan berhasil diperbarui.";
            return RedirectToAction(nameof(Index), new { group = option.GroupKey });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var option = await _formOptionRepository.GetByIdAsync(id);
            if (option is null) return NotFound();

            await _formOptionRepository.DeleteAsync(id);
            TempData["Success"] = "Pilihan berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { group = option.GroupKey });
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate, string group)
        {
            await _formOptionRepository.SetActiveAsync(id, activate);
            TempData["Success"] = activate ? "Pilihan diaktifkan." : "Pilihan dinonaktifkan.";
            return RedirectToAction(nameof(Index), new { group });
        }
    }
}