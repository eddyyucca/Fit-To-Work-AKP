using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class MitraFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Kode mitra wajib diisi")]
        public string Kode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama mitra wajib diisi")]
        [StringLength(150, ErrorMessage = "Nama maksimal 150 karakter")]
        public string Nama { get; set; } = string.Empty;

        [StringLength(250, ErrorMessage = "Keterangan maksimal 250 karakter")]
        public string? Keterangan { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master Data Mitra (perusahaan kontraktor / subkon). Kode 3 huruf dipakai sebagai awalan
    /// ID karyawan mitra, mis. GPE-3450.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/Mitras")]
    public class MitrasController : Controller
    {
        private static readonly Regex KodePattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

        private readonly IMitraRepository _repo;

        public MitrasController(IMitraRepository repo)
        {
            _repo = repo;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            return View(await _repo.GetAllAsync());
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(MitraFormViewModel model)
        {
            model.Kode = (model.Kode ?? string.Empty).Trim().ToUpperInvariant();
            model.Nama = (model.Nama ?? string.Empty).Trim();
            model.Keterangan = string.IsNullOrWhiteSpace(model.Keterangan) ? null : model.Keterangan.Trim();

            if (!KodePattern.IsMatch(model.Kode))
                ModelState.AddModelError(nameof(model.Kode), "Kode mitra harus 3 huruf (A-Z), mis. GPE.");
            else if (await _repo.KodeExistsAsync(model.Kode, model.Id > 0 ? model.Id : null))
                ModelState.AddModelError(nameof(model.Kode), $"Kode '{model.Kode}' sudah dipakai mitra lain.");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            if (model.Id > 0 && await _repo.GetByIdAsync(model.Id) is null)
                return NotFound();

            int renamed;
            try
            {
                renamed = await _repo.SaveAsync(new Mitra
                {
                    Id = model.Id,
                    Kode = model.Kode,
                    Nama = model.Nama,
                    Keterangan = model.Keterangan,
                    IsActive = model.IsActive
                });
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2627 or 2601)
            {
                // Unique key: kode sudah ada, atau NIP baru (KODE-NIP) bentrok dengan NIP karyawan lain
                TempData["Error"] = $"Gagal menyimpan: kode '{model.Kode}' atau ID karyawan {model.Kode}-xxx sudah dipakai data lain.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = renamed > 0
                ? $"Mitra {model.Kode} - {model.Nama} disimpan. ID {renamed} karyawan mitra ikut diganti ke kode baru."
                : $"Mitra {model.Kode} - {model.Nama} berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var item = await _repo.GetByIdAsync(id);
            if (item is null)
                return NotFound();
            await _repo.SetActiveAsync(id, activate);
            TempData["Success"] = $"Mitra {item.Kode} {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _repo.GetByIdAsync(id);
            if (item is null)
                return NotFound();

            if (await _repo.DeleteAsync(id))
                TempData["Success"] = $"Mitra {item.Kode} - {item.Nama} dihapus.";
            else
                TempData["Error"] = $"Mitra {item.Kode} masih punya {item.JumlahKaryawan} karyawan, jadi tidak bisa dihapus. Nonaktifkan saja.";
            return RedirectToAction(nameof(Index));
        }
    }
}
