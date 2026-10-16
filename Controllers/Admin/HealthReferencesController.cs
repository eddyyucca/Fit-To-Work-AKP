using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Menu "Acuan Nilai Pemkes": batas tensi, kolesterol, asam urat, gula darah, IMT, lingkar perut,
    /// batas positif tes alkohol & usulan tanggal follow up. Permission medical.master.
    /// Perubahan langsung berlaku untuk penanda warna (data lama ikut dinilai ulang dengan acuan baru).
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/HealthReferences")]
    public class HealthReferencesController : Controller
    {
        private readonly IHealthReferenceRepository _repo;

        public HealthReferencesController(IHealthReferenceRepository repo)
        {


            _repo = repo;
        }


        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Muat ulang supaya halaman selalu menampilkan isi database terbaru
            var overrides = await _repo.GetOverridesAsync();
            HealthRanges.Apply(overrides);
            ViewBag.Overrides = overrides;
            return View();
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromForm] Dictionary<string, decimal?> values)
        {
            var parsed = new Dictionary<string, decimal>();
            var errors = new List<string>();

            foreach (var d in HealthRanges.Definitions)
            {
                // Batas pemkes format lama tidak tampil di halaman ini: pertahankan nilai yang berlaku
                if (d.IsLegacy)
                {
                    parsed[d.Key] = HealthRanges.Current[d.Key];
                    continue;
                }
                if (!values.TryGetValue(d.Key, out var v) || v is null)
                {
                    errors.Add($"{d.Group}: \"{d.Label}\" wajib diisi.");
                    continue;
                }
                parsed[d.Key] = Math.Round(v.Value, d.Decimals);
            }

            if (errors.Count == 0)
                errors.AddRange(HealthRanges.Validate(parsed));

            if (errors.Count > 0)
            {
                TempData["Error"] = "Pengaturan TIDAK disimpan. " + string.Join(" ", errors);
                return RedirectToAction(nameof(Index));
            }

            await _repo.SaveAsync(parsed, User.AdminName());
            TempData["Success"] = "Batas tes alkohol & usulan follow up disimpan dan langsung berlaku.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Reset")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reset()
        {
            await _repo.ResetAsync();
            TempData["Success"] = "Batas tes alkohol & usulan follow up dikembalikan ke default.";
            return RedirectToAction(nameof(Index));
        }
    }
}
