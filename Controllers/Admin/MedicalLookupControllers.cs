using System.ComponentModel.DataAnnotations;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class MedicalLookupFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama wajib diisi")]
        [StringLength(100, ErrorMessage = "Nama maksimal 100 karakter")]
        public string Name { get; set; } = string.Empty;

        [Range(0, 9999, ErrorMessage = "Urutan 0 - 9999")]
        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>Data untuk view bersama Views/MedicalLookups/Index.cshtml.</summary>
    public class MedicalLookupPageViewModel
    {
        public string Controller { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ItemLabel { get; set; } = string.Empty;       // "kategori" / "satuan"
        public string Description { get; set; } = string.Empty;
        public string UsageLabel { get; set; } = string.Empty;      // "jenis penyakit" / "obat"
        public string UsageController { get; set; } = string.Empty; // halaman pemakai
        public string Placeholder { get; set; } = string.Empty;
        public int MaxLength { get; set; }
        public List<MedicalLookup> Items { get; set; } = new();
    }

    /// <summary>
    /// CRUD sederhana untuk master pilihan klinik (Kategori Penyakit, Satuan Obat).
    /// Nama yang masih dipakai tidak bisa dihapus, hanya dinonaktifkan (tidak muncul lagi di dropdown).
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    public abstract class MedicalLookupControllerBase : Controller
    {
        protected readonly IMedicalLookupRepository Repo;

        protected MedicalLookupControllerBase(IMedicalLookupRepository repo)
        {
            Repo = repo;
        }

        protected abstract MedicalLookupKind Kind { get; }
        protected abstract MedicalLookupPageViewModel Describe();

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var vm = Describe();
            vm.Items = await Repo.GetAllAsync(Kind);
            return View("~/Views/MedicalLookups/Index.cshtml", vm);
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(MedicalLookupFormViewModel model)
        {
            var info = Describe();
            model.Name = (model.Name ?? string.Empty).Trim();

            if (model.Name.Length > info.MaxLength)
                ModelState.AddModelError(nameof(model.Name), $"Nama maksimal {info.MaxLength} karakter");
            if (!string.IsNullOrEmpty(model.Name) && await Repo.NameExistsAsync(Kind, model.Name, model.Id > 0 ? model.Id : null))
                ModelState.AddModelError(nameof(model.Name), $"{Capitalize(info.ItemLabel)} '{model.Name}' sudah ada");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            if (model.Id > 0 && await Repo.GetByIdAsync(Kind, model.Id) is null)
                return NotFound();

            var renamed = await Repo.SaveAsync(Kind, new MedicalLookup
            {
                Id = model.Id,
                Name = model.Name,
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = renamed > 0
                ? $"{Capitalize(info.ItemLabel)} '{model.Name}' disimpan. {renamed} {info.UsageLabel} ikut diperbarui."
                : $"{Capitalize(info.ItemLabel)} '{model.Name}' berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var item = await Repo.GetByIdAsync(Kind, id);
            if (item is null)
                return NotFound();
            await Repo.SetActiveAsync(Kind, id, activate);
            TempData["Success"] = $"'{item.Name}' {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var info = Describe();
            var item = await Repo.GetByIdAsync(Kind, id);
            if (item is null)
                return NotFound();

            if (await Repo.DeleteAsync(Kind, id))
                TempData["Success"] = $"{Capitalize(info.ItemLabel)} '{item.Name}' dihapus.";
            else
                TempData["Error"] = $"'{item.Name}' masih dipakai {item.UsageCount} {info.UsageLabel}, jadi tidak bisa dihapus. Nonaktifkan saja.";
            return RedirectToAction(nameof(Index));
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    [Route("Admin/DiseaseCategories")]
    public class DiseaseCategoriesController : MedicalLookupControllerBase
    {
        public DiseaseCategoriesController(IMedicalLookupRepository repo) : base(repo) { }

        protected override MedicalLookupKind Kind => MedicalLookupKind.DiseaseCategory;

        protected override MedicalLookupPageViewModel Describe() => new()
        {
            Controller = "DiseaseCategories",
            Title = "Kategori Penyakit",
            ItemLabel = "kategori",
            Description = "Pilihan kategori di Master Jenis Penyakit (mis. Pernapasan, Pencernaan). Ganti nama = semua jenis penyakit dengan kategori itu ikut berganti.",
            UsageLabel = "jenis penyakit",
            UsageController = "Diseases",
            Placeholder = "mis. Pernapasan",
            MaxLength = 100
        };
    }

    [Route("Admin/MedicineUnits")]
    public class MedicineUnitsController : MedicalLookupControllerBase
    {
        public MedicineUnitsController(IMedicalLookupRepository repo) : base(repo) { }

        protected override MedicalLookupKind Kind => MedicalLookupKind.MedicineUnit;

        protected override MedicalLookupPageViewModel Describe() => new()
        {
            Controller = "MedicineUnits",
            Title = "Satuan Obat",
            ItemLabel = "satuan",
            Description = "Pilihan satuan di Master Obat (mis. tablet, botol). Ganti nama = semua obat dengan satuan itu ikut berganti; riwayat kunjungan lama tetap memakai satuan saat itu.",
            UsageLabel = "obat",
            UsageController = "Medicines",
            Placeholder = "mis. tablet",
            MaxLength = 30
        };
    }

    [Route("Admin/UrineParameters")]
    public class UrineParametersController : MedicalLookupControllerBase
    {
        public UrineParametersController(IMedicalLookupRepository repo) : base(repo) { }

        protected override MedicalLookupKind Kind => MedicalLookupKind.UrineParameter;

        protected override MedicalLookupPageViewModel Describe() => new()
        {
            Controller = "UrineParameters",
            Title = "Parameter Tes Urin",
            ItemLabel = "parameter",
            Description = "Daftar parameter yang diperiksa di Tes Urin (hasil Negatif/Positif), mis. Amphetamine (AMP). Ganti nama tidak mengubah hasil tes yang sudah tersimpan.",
            UsageLabel = "hasil tes",
            UsageController = "DrugTests",
            Placeholder = "mis. Amphetamine (AMP)",
            MaxLength = 100
        };
    }
}
