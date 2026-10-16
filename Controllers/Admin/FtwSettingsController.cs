using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Menu Fit To Work › Pengaturan Pengisian: kapan karyawan boleh mengisi FTW lagi (jeda sejak pengisian
    /// terakhir atau jam reset tetap), tombol "Isi Ulang", dan lama layar "sudah mengisi" di kiosk.
    /// Perubahan langsung berlaku (cache pengaturan dihapus saat disimpan).
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.FtwSettings)]
    [Route("Admin/FtwSettings")]
    public class FtwSettingsController : Controller
    {
        private readonly IFtwSettingRepository _repo;
        private readonly IAppClock _clock;

        public FtwSettingsController(IFtwSettingRepository repo, IAppClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        private async Task<IActionResult> ShowAsync(FtwSettingsFormViewModel model)
        {
            ViewBag.Current = await _repo.GetAsync();
            ViewBag.Info = await _repo.GetInfoAsync();
            ViewBag.TimeZone = _clock.TimeZoneId;
            return View("Index", model);
        }

        [HttpGet("")]
        public async Task<IActionResult> Index() =>
            await ShowAsync(FtwSettingsFormViewModel.From(await _repo.GetAsync()));

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(FtwSettingsFormViewModel model)
        {
            ModelState.Clear();   // semua validasi di FtwRules.Validate (pesan bahasa Indonesia, satu tempat)

            var errors = new List<string>();
            if (!Enum.IsDefined(model.Mode))
                errors.Add("Pilih cara reset pengisian.");

            var jam = model.JedaJam ?? 0;
            var menit = model.JedaMenit ?? 0;
            if (jam < 0 || menit is < 0 or > 59)
                errors.Add("Jeda tidak valid (menit 0–59).");

            var times = new List<TimeSpan>();
            foreach (var raw in model.JamReset.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (FtwRules.TryParseTime(raw, out var t))
                    times.Add(t);
                else
                    errors.Add($"Jam reset \"{raw}\" tidak valid (format JJ:MM).");
            }
            times.Sort();

            // Jam reset hanya wajib di mode JamTetap; di mode Jeda tetap disimpan supaya tidak hilang
            // saat admin berpindah mode. Kalau kosong, pakai default (06:00, 18:00).
            if (model.Mode != FtwResetMode.JamTetap && times.Count == 0)
                times = FtwSettings.Default.JamReset.ToList();

            var settings = new FtwSettings(
                model.Mode,
                jam * 60 + menit,
                times,
                model.IzinIsiUlang,
                model.DetikKembali ?? FtwSettings.Default.DetikKembali);

            if (errors.Count == 0)
                errors.AddRange(FtwRules.Validate(settings));

            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    ModelState.AddModelError(string.Empty, e);
                return await ShowAsync(model);
            }

            await _repo.SaveAsync(settings, User.AdminName());
            TempData["Success"] = "Pengaturan pengisian Fit To Work disimpan dan langsung berlaku. " + FtwRules.Describe(settings) + ".";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Reset")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reset()
        {
            await _repo.ResetAsync();
            TempData["Success"] = "Pengaturan pengisian Fit To Work dikembalikan ke default (jeda 12 jam, Isi Ulang diizinkan, 8 detik).";
            return RedirectToAction(nameof(Index));
        }
    }
}
