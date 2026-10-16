using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    public class MitraEmployeeFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Pilih mitra")]
        public int? MitraId { get; set; }

        [Required(ErrorMessage = "NIP mitra wajib diisi")]
        public string MitraNip { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama wajib diisi")]
        [StringLength(150, ErrorMessage = "Nama maksimal 150 karakter")]
        public string Nama { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Jabatan maksimal 100 karakter")]
        public string? MitraJabatan { get; set; }

        public string? JenisKelamin { get; set; }
        public DateTime? TanggalLahir { get; set; }

        [StringLength(20, ErrorMessage = "Nomor HP maksimal 20 karakter")]
        public string? NomorHp { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class MitraEmployeeIndexViewModel
    {
        public int? MitraId { get; set; }
        public List<Mitra> Mitras { get; set; } = new();
        public List<Employee> Employees { get; set; } = new();
    }

    /// <summary>
    /// Karyawan mitra kerja, diinput oleh tim medis (hak akses Kunjungan Klinik).
    /// ID = kode mitra + "-" + NIP mitra, mis. GPE-3450. Karyawan mitra hanya dipakai untuk
    /// kunjungan klinik: tidak bisa login Fit To Work, Daftar Gym, maupun Riwayat Saya.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalVisit)]
    [Route("Admin/MitraEmployees")]
    public class MitraEmployeesController : Controller
    {
        // NIP mitra: huruf/angka, boleh titik / garis miring / minus di tengah (mis. 3450, 21.0045, K-12)
        private static readonly Regex NipPattern = new(@"^[A-Za-z0-9]([A-Za-z0-9./-]{0,14}[A-Za-z0-9])?$", RegexOptions.Compiled);
        private const int MaxMitraNip = 16;   // 3 kode + "-" + 16 = 20 = panjang kolom Employees.NIP

        private readonly IEmployeeRepository _employeeRepository;
        private readonly IMitraRepository _mitraRepository;
        private readonly IAppClock _clock;

        public MitraEmployeesController(IEmployeeRepository employeeRepository, IMitraRepository mitraRepository, IAppClock clock)
        {
            _employeeRepository = employeeRepository;
            _mitraRepository = mitraRepository;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(int? mitraId)
        {
            var vm = new MitraEmployeeIndexViewModel
            {
                MitraId = mitraId,
                Mitras = await _mitraRepository.GetAllAsync(),
                Employees = await _employeeRepository.GetMitraEmployeesAsync(mitraId)
            };
            return View(vm);
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(MitraEmployeeFormViewModel model, int? returnMitraId)
        {
            model.MitraNip = (model.MitraNip ?? string.Empty).Trim().ToUpperInvariant();
            model.Nama = (model.Nama ?? string.Empty).Trim();
            model.MitraJabatan = string.IsNullOrWhiteSpace(model.MitraJabatan) ? null : model.MitraJabatan.Trim();
            model.NomorHp = string.IsNullOrWhiteSpace(model.NomorHp) ? null : model.NomorHp.Trim();
            model.JenisKelamin = model.JenisKelamin is "L" or "P" ? model.JenisKelamin : null;

            Employee? existing = null;
            if (model.Id > 0)
            {
                existing = await _employeeRepository.GetByIdAsync(model.Id);
                if (existing is null || !existing.IsMitra)
                    return NotFound();
            }

            var mitra = model.MitraId.HasValue ? await _mitraRepository.GetByIdAsync(model.MitraId.Value) : null;
            if (model.MitraId.HasValue && mitra is null)
                ModelState.AddModelError(nameof(model.MitraId), "Mitra tidak ditemukan.");
            // Mitra nonaktif tidak boleh dipakai untuk karyawan baru / pindah mitra
            else if (mitra is { IsActive: false } && existing?.MitraId != mitra.Id)
                ModelState.AddModelError(nameof(model.MitraId), $"Mitra {mitra.Kode} sedang nonaktif.");

            if (model.MitraNip.Length > MaxMitraNip || !NipPattern.IsMatch(model.MitraNip))
                ModelState.AddModelError(nameof(model.MitraNip), $"NIP mitra hanya huruf/angka (boleh . / - di tengah), maksimal {MaxMitraNip} karakter.");

            if (model.TanggalLahir.HasValue && (model.TanggalLahir.Value.Date > _clock.Today || model.TanggalLahir.Value.Year < 1900))
                ModelState.AddModelError(nameof(model.TanggalLahir), "Tanggal lahir tidak valid.");

            string? fullId = null;
            if (ModelState.IsValid && mitra is not null)
            {
                fullId = Mitra.BuildId(mitra.Kode, model.MitraNip);
                var dup = await _employeeRepository.GetByNIPAsync(fullId);
                if (dup is not null && dup.Id != model.Id)
                    ModelState.AddModelError(nameof(model.MitraNip), $"ID {fullId} sudah dipakai ({dup.Nama}).");
            }

            if (!ModelState.IsValid || mitra is null || fullId is null)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { mitraId = returnMitraId });
            }

            var employee = new Employee
            {
                Id = model.Id,
                NIP = fullId,
                Nama = model.Nama,
                NomorHp = model.NomorHp,
                TanggalLahir = model.TanggalLahir?.Date,
                JenisKelamin = model.JenisKelamin,
                IsActive = model.IsActive,
                MitraId = mitra.Id,
                MitraNip = model.MitraNip,
                MitraJabatan = model.MitraJabatan
            };

            try
            {
                if (model.Id > 0)
                    await _employeeRepository.UpdateMitraEmployeeAsync(employee);
                else
                    await _employeeRepository.CreateMitraEmployeeAsync(employee);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2627 or 2601)
            {
                TempData["Error"] = $"ID {fullId} sudah dipakai karyawan lain.";
                return RedirectToAction(nameof(Index), new { mitraId = returnMitraId });
            }

            TempData["Success"] = $"Karyawan mitra {fullId} - {model.Nama} berhasil disimpan.";
            return RedirectToAction(nameof(Index), new { mitraId = returnMitraId });
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate, int? returnMitraId)
        {
            var e = await _employeeRepository.GetByIdAsync(id);
            if (e is null || !e.IsMitra)
                return NotFound();
            await _employeeRepository.SetActiveAsync(id, activate);
            TempData["Success"] = $"{e.NIP} - {e.Nama} {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index), new { mitraId = returnMitraId });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int? returnMitraId)
        {
            var e = await _employeeRepository.GetByIdAsync(id);
            if (e is null || !e.IsMitra)
                return NotFound();

            if (await _employeeRepository.DeleteMitraEmployeeAsync(id))
                TempData["Success"] = $"{e.NIP} - {e.Nama} dihapus.";
            else
                TempData["Error"] = $"{e.NIP} - {e.Nama} sudah punya riwayat kunjungan klinik / tes urin & alkohol, jadi tidak bisa dihapus. Nonaktifkan saja.";
            return RedirectToAction(nameof(Index), new { mitraId = returnMitraId });
        }
    }
}
