using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class ActivityTypeFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Kode wajib diisi")]
        [RegularExpression("^[A-Za-z0-9_]{2,20}$", ErrorMessage = "Kode 2-20 karakter: huruf, angka atau _ (tanpa spasi)")]
        public string Kode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama aktivitas wajib diisi")]
        [StringLength(60, ErrorMessage = "Nama maksimal 60 karakter")]
        [Display(Name = "Nama Aktivitas")]
        public string Nama { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ikon wajib diisi")]
        [StringLength(16, ErrorMessage = "Ikon maksimal 1-2 emoji")]
        public string Ikon { get; set; } = "🏅";

        [Display(Name = "Rekam jarak dengan GPS")]
        public bool PakaiGps { get; set; }

        [Display(Name = "Wajib isi berat badan saat mulai")]
        public bool WajibBerat { get; set; }

        [Required(ErrorMessage = "Batas jam wajib diisi")]
        [Range(ActivityType.MinJam, ActivityType.MaxJam, ErrorMessage = "Batas jam antara 1 dan 12")]
        [Display(Name = "Batas sesi (jam)")]
        public int? MaksJam { get; set; } = 3;

        [Required]
        [Display(Name = "Kunci lokasi")]
        public string KunciLokasi { get; set; } = ActivityLocationLock.Mulai;

        [Range(typeof(decimal), "5", "120", ErrorMessage = "Batas kecepatan antara 5 dan 120 km/jam")]
        [Display(Name = "Batas kecepatan GPS (km/jam)")]
        public decimal? MaksKecepatanKmh { get; set; }

        [Range(0, 999, ErrorMessage = "Urutan 0 - 999")]
        [Display(Name = "Urutan tampil")]
        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master Jenis Aktivitas Kebugaran (Gym, Lari, Hiking, ...). Riwayat menyimpan snapshot nama & aturan,
    /// jadi mengubah jenis aktivitas tidak mengubah riwayat lama maupun sesi yang sedang berjalan.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.FitnessTypes)]
    [Route("Admin/ActivityTypes")]
    public class ActivityTypesController : Controller
    {
        private readonly IActivityTypeRepository _repo;

        public ActivityTypesController(IActivityTypeRepository repo)
        {
            _repo = repo;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index() => View(await _repo.GetAllAsync());

        [HttpGet("Create")]
        public IActionResult Create() => View("Edit", new ActivityTypeFormViewModel());

        [HttpGet("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null)
                return NotFound();

            return View(new ActivityTypeFormViewModel
            {
                Id = t.Id,
                Kode = t.Kode,
                Nama = t.Nama,
                Ikon = t.Ikon,
                PakaiGps = t.PakaiGps,
                WajibBerat = t.WajibBerat,
                MaksJam = t.MaksJam,
                KunciLokasi = t.KunciLokasi,
                MaksKecepatanKmh = t.MaksKecepatanKmh,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive
            });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(ActivityTypeFormViewModel model)
        {
            ActivityType? existing = null;
            if (model.Id > 0)
            {
                existing = await _repo.GetByIdAsync(model.Id);
                if (existing is null)
                    return NotFound();
                // Kode tidak bisa diubah setelah dibuat
                model.Kode = existing.Kode;
                ModelState.Remove(nameof(model.Kode));
            }
            else
            {
                model.Kode = (model.Kode ?? string.Empty).Trim().ToUpperInvariant();
                if (Regex.IsMatch(model.Kode, "^[A-Z0-9_]{2,20}$") && await _repo.KodeExistsAsync(model.Kode, 0))
                    ModelState.AddModelError(nameof(model.Kode), $"Kode {model.Kode} sudah dipakai.");
            }

            model.Nama = (model.Nama ?? string.Empty).Trim();
            model.Ikon = (model.Ikon ?? string.Empty).Trim();
            if (model.Ikon.Length > 0 && new StringInfo(model.Ikon).LengthInTextElements > 2)
                ModelState.AddModelError(nameof(model.Ikon), "Ikon cukup 1 emoji (mis. 🏃).");
            if (!ActivityLocationLock.IsValid(model.KunciLokasi))
                ModelState.AddModelError(nameof(model.KunciLokasi), "Pilih kunci lokasi.");
            if (!model.PakaiGps)
                model.MaksKecepatanKmh = null;
            else if (model.MaksKecepatanKmh is null)
                ModelState.AddModelError(nameof(model.MaksKecepatanKmh), "Isi batas kecepatan untuk aktivitas ber-GPS (mis. lari 25, sepeda 60).");

            if (!ModelState.IsValid)
                return View("Edit", model);

            await _repo.SaveAsync(new ActivityType
            {
                Id = model.Id,
                Kode = model.Kode,
                Nama = model.Nama,
                Ikon = model.Ikon,
                PakaiGps = model.PakaiGps,
                WajibBerat = model.WajibBerat,
                MaksJam = model.MaksJam!.Value,
                KunciLokasi = model.KunciLokasi,
                MaksKecepatanKmh = model.MaksKecepatanKmh is decimal k ? Math.Round(k, 1) : null,
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            TempData["Success"] = $"Jenis aktivitas {model.Nama} berhasil disimpan." +
                (existing is not null ? " Sesi yang sedang berjalan tetap memakai aturan lama sampai selesai." : "");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null)
                return NotFound();

            await _repo.SetActiveAsync(id, activate);
            TempData["Success"] = $"Jenis aktivitas {t.Nama} {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var t = await _repo.GetByIdAsync(id);
            if (t is null)
                return NotFound();

            if (await _repo.DeleteIfUnusedAsync(id))
                TempData["Success"] = $"Jenis aktivitas {t.Nama} dihapus.";
            else
                TempData["Error"] = $"{t.Nama} sudah punya riwayat, jadi tidak bisa dihapus. Nonaktifkan saja supaya tidak muncul di pilihan karyawan.";
            return RedirectToAction(nameof(Index));
        }
    }
}
