using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FitToWorkApp.Controllers.Admin
{
    public class CriterionRow
    {
        public string? Label { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public string? MatchText { get; set; }
        public string? ForSex { get; set; }
        public int? Points { get; set; }
        public string? Level { get; set; }
    }

    public class CheckupParameterFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama parameter wajib diisi")]
        [StringLength(100, ErrorMessage = "Nama maksimal 100 karakter")]
        public string Name { get; set; } = string.Empty;

        [StringLength(30, ErrorMessage = "Satuan maksimal 30 karakter")]
        public string? Unit { get; set; }

        public string InputType { get; set; } = CheckupInputTypes.Number;

        [Range(0, 3, ErrorMessage = "Desimal 0 - 3")]
        public int Decimals { get; set; }

        public decimal? MinInput { get; set; }
        public decimal? MaxInput { get; set; }
        public bool IsScored { get; set; } = true;

        [StringLength(250, ErrorMessage = "Keterangan maksimal 250 karakter")]
        public string? Hint { get; set; }

        [Range(0, 9999, ErrorMessage = "Urutan 0 - 9999")]
        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public List<CriterionRow> Criteria { get; set; } = new();

        public int UsageCount { get; set; }
    }

    public class RiskLevelRow
    {
        public int? MinScore { get; set; }
        public int? MaxScore { get; set; }
        public string? Label { get; set; }
        public string? Color { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Menu "Parameter & Skor Pemkes": isian pemeriksaan kesehatan, kriteria + poin + warna, dan interpretasi total skor.
    /// Perubahan hanya berlaku untuk pemeriksaan BARU; pemeriksaan lama menyimpan snapshot nilai, kriteria & poinnya.
    /// Permission medical.master.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/CheckupParameters")]
    public class CheckupParametersController : Controller
    {
        private readonly ICheckupParameterRepository _repo;

        public CheckupParametersController(ICheckupParameterRepository repo)
        {
            _repo = repo;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewBag.RiskLevels = await _repo.GetRiskLevelsAsync();
            return View(await _repo.GetAllAsync(onlyActive: false));
        }

        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            var all = await _repo.GetAllAsync(onlyActive: false);
            var model = new CheckupParameterFormViewModel
            {
                SortOrder = (all.Count == 0 ? 0 : all.Max(p => p.SortOrder)) + 10,
                Criteria = new List<CriterionRow> { new() }
            };
            return View("Edit", model);
        }

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var p = await _repo.GetByIdAsync(id);
            if (p is null)
                return NotFound();

            return View(new CheckupParameterFormViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Unit = p.Unit,
                InputType = p.InputType,
                Decimals = p.Decimals,
                MinInput = p.MinInput,
                MaxInput = p.MaxInput,
                IsScored = p.IsScored,
                Hint = p.Hint,
                SortOrder = p.SortOrder,
                IsActive = p.IsActive,
                UsageCount = p.UsageCount,
                Criteria = p.Criteria.Select(c => new CriterionRow
                {
                    Label = c.Label, MinValue = c.MinValue, MaxValue = c.MaxValue, MatchText = c.MatchText,
                    ForSex = c.ForSex, Points = c.Points, Level = c.Level
                }).ToList()
            });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(CheckupParameterFormViewModel model)
        {
            model.Name = (model.Name ?? string.Empty).Trim();
            model.Unit = Clean(model.Unit);
            model.Hint = Clean(model.Hint);

            if (!CheckupInputTypes.All.Contains(model.InputType))
                ModelState.AddModelError(nameof(model.InputType), "Tipe isian tidak valid");
            if (!string.IsNullOrEmpty(model.Name) && await _repo.NameExistsAsync(model.Name, model.Id > 0 ? model.Id : null))
                ModelState.AddModelError(nameof(model.Name), $"Parameter '{model.Name}' sudah ada");
            if (model.MinInput.HasValue && model.MaxInput.HasValue && model.MinInput > model.MaxInput)
                ModelState.AddModelError(nameof(model.MinInput), "Batas input minimum lebih besar dari maksimum");
            if (model.InputType != CheckupInputTypes.Number)
            {
                model.MinInput = null;
                model.MaxInput = null;
            }
            if (model.InputType is CheckupInputTypes.Choice or CheckupInputTypes.Sex or CheckupInputTypes.Age)
                model.Decimals = 0;
            if (model.InputType == CheckupInputTypes.Bmi && model.Decimals == 0)
                model.Decimals = 1;

            var criteria = ValidateCriteria(model, ModelState);

            if (model.Id > 0 && await _repo.GetByIdAsync(model.Id) is null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                if (model.Criteria.Count == 0)
                    model.Criteria.Add(new CriterionRow());
                return View("Edit", model);
            }

            await _repo.SaveAsync(new CheckupParameter
            {
                Id = model.Id,
                Name = model.Name,
                Unit = model.Unit,
                InputType = model.InputType,
                Decimals = model.Decimals,
                MinInput = model.MinInput,
                MaxInput = model.MaxInput,
                IsScored = model.IsScored,
                Hint = model.Hint,
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            }, criteria);

            TempData["Success"] = $"Parameter '{model.Name}' disimpan. Berlaku untuk pemeriksaan baru.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Cek & rapikan kriteria sesuai tipe isian. Baris kosong diabaikan.</summary>
        private static List<CheckupCriterion> ValidateCriteria(CheckupParameterFormViewModel model, ModelStateDictionary ms)
        {
            var result = new List<CheckupCriterion>();
            var numeric = CheckupInputTypes.IsNumeric(model.InputType);
            var rows = model.Criteria
                .Where(r => !string.IsNullOrWhiteSpace(r.Label) || r.MinValue.HasValue || r.MaxValue.HasValue || !string.IsNullOrEmpty(r.MatchText))
                .ToList();

            var no = 0;
            foreach (var r in rows)
            {
                no++;
                var label = Clean(r.Label);
                var level = CheckupLevels.All.Contains(r.Level ?? "") ? r.Level ?? "" : CheckupLevels.None;
                var forSex = r.ForSex is "L" or "P" ? r.ForSex : null;
                var points = r.Points ?? 0;

                if (points is < -100 or > 100)
                    ms.AddModelError(string.Empty, $"Kriteria {no}: poin harus antara -100 dan 100.");

                if (numeric)
                {
                    if (r.MinValue is null && r.MaxValue is null)
                    {
                        ms.AddModelError(string.Empty, $"Kriteria {no}: isi batas bawah dan/atau batas atas.");
                        continue;
                    }
                    if (r.MinValue.HasValue && r.MaxValue.HasValue && r.MinValue > r.MaxValue)
                        ms.AddModelError(string.Empty, $"Kriteria {no}: batas bawah lebih besar dari batas atas.");
                    label ??= r.MinValue.HasValue && r.MaxValue.HasValue ? $"{Fmt(r.MinValue)} – {Fmt(r.MaxValue)}"
                        : r.MinValue.HasValue ? $"≥ {Fmt(r.MinValue)}" : $"≤ {Fmt(r.MaxValue)}";
                    result.Add(new CheckupCriterion { Label = label, MinValue = r.MinValue, MaxValue = r.MaxValue, ForSex = forSex, Points = points, Level = level });
                }
                else if (model.InputType == CheckupInputTypes.Sex)
                {
                    if (r.MatchText is not ("L" or "P"))
                    {
                        ms.AddModelError(string.Empty, $"Kriteria {no}: pilih jenis kelamin L / P.");
                        continue;
                    }
                    label ??= r.MatchText == "L" ? "Laki-laki" : "Perempuan";
                    result.Add(new CheckupCriterion { Label = label, MatchText = r.MatchText, Points = points, Level = level });
                }
                else
                {
                    if (label is null)
                    {
                        ms.AddModelError(string.Empty, $"Kriteria {no}: teks pilihan wajib diisi.");
                        continue;
                    }
                    result.Add(new CheckupCriterion { Label = label, Points = points, Level = level });
                }
            }

            if (result.Any(c => c.Label.Length > 100))
                ms.AddModelError(string.Empty, "Teks kriteria maksimal 100 karakter.");

            switch (model.InputType)
            {
                case CheckupInputTypes.Choice when result.Count < 2:
                    ms.AddModelError(string.Empty, "Parameter pilihan minimal punya 2 pilihan.");
                    break;
                case CheckupInputTypes.Choice when result.GroupBy(c => c.Label, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1):
                    ms.AddModelError(string.Empty, "Ada teks pilihan yang sama.");
                    break;
                case CheckupInputTypes.Sex when result.GroupBy(c => c.MatchText).Any(g => g.Count() > 1):
                    ms.AddModelError(string.Empty, "Jenis kelamin L / P masing-masing cukup satu kriteria.");
                    break;
                case not CheckupInputTypes.Choice when result.Count == 0:
                    ms.AddModelError(string.Empty, "Isi minimal satu kriteria.");
                    break;
            }

            // Rentang angka tidak boleh tumpang tindih (untuk jenis kelamin yang sama)
            if (numeric)
            {
                for (int i = 0; i < result.Count; i++)
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        var a = result[i];
                        var b = result[j];
                        var sameScope = a.ForSex is null || b.ForSex is null || a.ForSex == b.ForSex;
                        var overlap = (a.MinValue ?? decimal.MinValue) <= (b.MaxValue ?? decimal.MaxValue)
                                      && (b.MinValue ?? decimal.MinValue) <= (a.MaxValue ?? decimal.MaxValue);
                        if (sameScope && overlap)
                            ms.AddModelError(string.Empty, $"Rentang \"{a.Label}\" dan \"{b.Label}\" tumpang tindih.");
                    }
            }

            return result;
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var p = await _repo.GetByIdAsync(id);
            if (p is null)
                return NotFound();
            await _repo.SetActiveAsync(id, activate);
            TempData["Success"] = $"Parameter '{p.Name}' {(activate ? "diaktifkan" : "dinonaktifkan (tidak muncul lagi di form pemkes)")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await _repo.GetByIdAsync(id);
            if (p is null)
                return NotFound();
            if (await _repo.DeleteAsync(id))
                TempData["Success"] = $"Parameter '{p.Name}' dihapus.";
            else
                TempData["Error"] = $"'{p.Name}' sudah dipakai di {p.UsageCount} pemeriksaan, jadi tidak bisa dihapus. Nonaktifkan saja.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("RiskLevels")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RiskLevels(List<RiskLevelRow> levels)
        {
            var errors = new List<string>();
            var list = new List<CheckupRiskLevel>();
            foreach (var r in levels.Where(r => r.MinScore.HasValue || !string.IsNullOrWhiteSpace(r.Label)))
            {
                var label = Clean(r.Label);
                if (r.MinScore is null || label is null)
                {
                    errors.Add("Setiap kategori wajib punya skor minimum dan nama.");
                    continue;
                }
                if (r.MaxScore.HasValue && r.MaxScore < r.MinScore)
                    errors.Add($"\"{label}\": skor maksimum lebih kecil dari minimum.");
                if (label.Length > 50)
                    errors.Add($"\"{label}\": nama maksimal 50 karakter.");
                list.Add(new CheckupRiskLevel
                {
                    MinScore = r.MinScore.Value,
                    MaxScore = r.MaxScore,
                    Label = label,
                    Color = RiskColors.All.Contains(r.Color ?? "") ? r.Color! : "secondary",
                    Description = Truncate(Clean(r.Description), 250)
                });
            }

            var sorted = list.OrderBy(l => l.MinScore).ToList();
            for (int i = 1; i < sorted.Count; i++)
                if (sorted[i - 1].MaxScore is null || sorted[i - 1].MaxScore >= sorted[i].MinScore)
                    errors.Add($"Rentang \"{sorted[i - 1].Label}\" dan \"{sorted[i].Label}\" tumpang tindih.");
            if (sorted.Count == 0)
                errors.Add("Isi minimal satu kategori risiko.");

            if (errors.Count > 0)
            {
                TempData["Error"] = "Kategori risiko TIDAK disimpan. " + string.Join(" ", errors.Distinct());
                return RedirectToAction(nameof(Index));
            }

            await _repo.SaveRiskLevelsAsync(sorted);
            TempData["Success"] = "Kategori risiko (interpretasi total skor) disimpan. Berlaku untuk pemeriksaan baru.";
            return RedirectToAction(nameof(Index));
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
        private static string Fmt(decimal? v) => v?.ToString("0.###", CultureInfo.GetCultureInfo("id-ID")) ?? "";
    }
}
