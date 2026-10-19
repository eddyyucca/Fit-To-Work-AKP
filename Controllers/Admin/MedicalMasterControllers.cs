using System.ComponentModel.DataAnnotations;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers.Admin
{
    // ======================= Master Jenis Penyakit =======================

    public class DiseaseFormViewModel
    {
        public int Id { get; set; }

        [StringLength(20)]
        public string? Kode { get; set; }

        [Required(ErrorMessage = "Nama penyakit wajib diisi")]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Nama Inggris asli ICD-10 WHO (opsional).</summary>
        [StringLength(200)]
        public string? NameEn { get; set; }

        [StringLength(100)]
        public string? Kategori { get; set; }

        public bool IsActive { get; set; } = true;
    }

    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/Diseases")]
    public class DiseasesController : Controller
    {
        private readonly IMedicalMasterRepository _repo;
        private readonly IMedicalLookupRepository _lookups;

        public DiseasesController(IMedicalMasterRepository repo, IMedicalLookupRepository lookups)
        {
            _repo = repo;
            _lookups = lookups;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Dropdown kategori dari Master Data > Kategori Penyakit (yang aktif)
            ViewBag.KategoriOptions = await _lookups.GetNamesAsync(MedicalLookupKind.DiseaseCategory);
            return View(await _repo.GetDiseasesAsync());
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(DiseaseFormViewModel model)
        {
            model.Name = model.Name?.Trim() ?? string.Empty;
            model.Kategori = string.IsNullOrWhiteSpace(model.Kategori) ? null : model.Kategori.Trim();
            if (!string.IsNullOrEmpty(model.Name) && await _repo.DiseaseNameExistsAsync(model.Name, model.Id > 0 ? model.Id : null))
                ModelState.AddModelError(nameof(model.Name), "Nama penyakit sudah ada");

            var existing = model.Id > 0 ? await _repo.GetDiseaseByIdAsync(model.Id) : null;
            if (model.Id > 0 && existing is null)
                return NotFound();

            // Kategori wajib dari master (kategori lama yang sudah dinonaktifkan tetap boleh dipertahankan)
            if (model.Kategori is not null && model.Kategori != existing?.Kategori
                && !await _lookups.IsValidAsync(MedicalLookupKind.DiseaseCategory, model.Kategori))
                ModelState.AddModelError(nameof(model.Kategori), $"Kategori '{model.Kategori}' tidak ada di Master Kategori Penyakit");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            await _repo.SaveDiseaseAsync(new Disease
            {
                Id = model.Id,
                Kode = string.IsNullOrWhiteSpace(model.Kode) ? null : model.Kode.Trim().ToUpperInvariant(),
                Name = model.Name,
                NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? null : model.NameEn.Trim(),
                Kategori = model.Kategori,
                IsActive = model.IsActive
            });

            TempData["Success"] = $"Jenis penyakit '{model.Name}' berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        // Tidak ada hapus permanen: penyakit yang sudah dipakai di rekam medis cukup dinonaktifkan
        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var d = await _repo.GetDiseaseByIdAsync(id);
            if (d is null)
                return NotFound();
            await _repo.SetDiseaseActiveAsync(id, activate);
            TempData["Success"] = $"'{d.Name}' {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }
    }

    // ======================= Master Obat & Stok =======================

    public class MedicineFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama obat wajib diisi")]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Satuan wajib diisi")]
        [StringLength(30)]
        public string Satuan { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Keterangan { get; set; }

        [Range(0, 1000000, ErrorMessage = "Stok minimum tidak valid")]
        public int StokMinimum { get; set; }

        [Range(0, 1000000, ErrorMessage = "Stok awal tidak valid")]
        public int StokAwal { get; set; }     // hanya dipakai saat tambah obat baru

        public bool IsActive { get; set; } = true;
    }

    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/Medicines")]
    public class MedicinesController : Controller
    {
        private static readonly TimeSpan ImportPreviewLifetime = TimeSpan.FromMinutes(30);

        private readonly IMedicalMasterRepository _repo;
        private readonly IMedicalLookupRepository _lookups;
        private readonly IMemoryCache _cache;
        private readonly IAppClock _clock;

        public MedicinesController(IMedicalMasterRepository repo, IMedicalLookupRepository lookups, IMemoryCache cache, IAppClock clock)
        {
            _repo = repo;
            _lookups = lookups;
            _cache = cache;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Dropdown satuan dari Master Data > Satuan Obat (yang aktif)
            ViewBag.SatuanOptions = await _lookups.GetNamesAsync(MedicalLookupKind.MedicineUnit);
            return View(await _repo.GetMedicinesAsync());
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(MedicineFormViewModel model)
        {
            model.Name = model.Name?.Trim() ?? string.Empty;
            model.Satuan = model.Satuan?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(model.Name) && await _repo.MedicineNameExistsAsync(model.Name, model.Id > 0 ? model.Id : null))
                ModelState.AddModelError(nameof(model.Name), "Nama obat sudah ada");

            var existing = model.Id > 0 ? await _repo.GetMedicineByIdAsync(model.Id) : null;
            if (model.Id > 0 && existing is null)
                return NotFound();

            // Satuan wajib dari master (satuan lama yang sudah dinonaktifkan tetap boleh dipertahankan)
            if (!string.IsNullOrEmpty(model.Satuan) && model.Satuan != existing?.Satuan
                && !await _lookups.IsValidAsync(MedicalLookupKind.MedicineUnit, model.Satuan))
                ModelState.AddModelError(nameof(model.Satuan), $"Satuan '{model.Satuan}' tidak ada di Master Satuan Obat");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            var medicine = new Medicine
            {
                Id = model.Id,
                Name = model.Name,
                Satuan = model.Satuan,
                Keterangan = string.IsNullOrWhiteSpace(model.Keterangan) ? null : model.Keterangan.Trim(),
                StokMinimum = model.StokMinimum,
                IsActive = model.IsActive
            };

            if (model.Id > 0)
            {
                await _repo.UpdateMedicineAsync(medicine);
            }
            else
            {
                await _repo.CreateMedicineAsync(medicine, model.StokAwal, User.AdminId(), User.AdminName());
            }

            TempData["Success"] = $"Obat '{model.Name}' berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        // POST Admin/Medicines/Stock/5  mode=masuk (qty = jumlah masuk) | mode=opname (qty = stok fisik)
        [HttpPost("Stock/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stock(int id, string mode, int qty, string? keterangan)
        {
            var med = await _repo.GetMedicineByIdAsync(id);
            if (med is null)
                return NotFound();

            try
            {
                if (mode == "opname")
                {
                    var selisih = await _repo.AdjustStockAsync(id, qty, keterangan, User.AdminId(), User.AdminName());
                    TempData["Success"] = selisih == 0
                        ? $"Stok {med.Name} sudah sesuai ({qty} {med.Satuan}), tidak ada perubahan."
                        : $"Stok {med.Name} disesuaikan menjadi {qty} {med.Satuan} (selisih {selisih:+#;-#;0}).";
                }
                else
                {
                    var stok = await _repo.AddStockAsync(id, qty, keterangan, User.AdminId(), User.AdminName());
                    TempData["Success"] = $"Stok {med.Name} bertambah {qty} {med.Satuan}. Stok sekarang {stok}.";
                }
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ToggleActive/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool activate)
        {
            var med = await _repo.GetMedicineByIdAsync(id);
            if (med is null)
                return NotFound();
            await _repo.SetMedicineActiveAsync(id, activate);
            TempData["Success"] = $"'{med.Name}' {(activate ? "diaktifkan" : "dinonaktifkan")}.";
            return RedirectToAction(nameof(Index));
        }

        // ======================= Import stok dari Excel =======================
        // Alur: unduh template (berisi daftar obat & stok saat ini) -> isi Jumlah -> upload -> pratinjau -> Simpan.
        // Hasil pembacaan file disimpan sementara di memori server (30 menit) sampai admin menekan Simpan.

        [HttpGet("Import")]
        public IActionResult Import() => View();

        [HttpGet("Import/Template")]
        public async Task<IActionResult> ImportTemplate()
        {
            var medicines = await _repo.GetMedicinesAsync(onlyActive: true);
            var satuan = await _lookups.GetNamesAsync(MedicalLookupKind.MedicineUnit);
            var bytes = MedicineStockImport.BuildTemplate(medicines, satuan, _clock.LocalNow);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Stok_Obat_{_clock.Today:yyyyMMdd}.xlsx");
        }

        [HttpPost("Import")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10L * 1024 * 1024)]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            if (file is null || file.Length == 0)
            {
                TempData["Error"] = "Pilih file Excel (.xlsx) terlebih dahulu.";
                return RedirectToAction(nameof(Import));
            }
            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Format file harus .xlsx (Excel).";
                return RedirectToAction(nameof(Import));
            }

            var medicines = await _repo.GetMedicinesAsync();
            var satuan = await _lookups.GetNamesAsync(MedicalLookupKind.MedicineUnit);

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;
            var result = MedicineStockImport.Read(stream, Path.GetFileName(file.FileName), medicines, satuan);

            // Simpan hasil sementara untuk langkah "Simpan" (kunci diikat ke admin yang upload)
            var token = Guid.NewGuid().ToString("N");
            _cache.Set(CacheKey(token), result, ImportPreviewLifetime);
            ViewBag.Token = token;
            return View("ImportPreview", result);
        }

        [HttpPost("Import/Confirm")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportConfirm(string token)
        {
            if (string.IsNullOrEmpty(token) || !_cache.TryGetValue(CacheKey(token), out StockImportResult? result) || result is null)
            {
                TempData["Error"] = "Pratinjau import sudah kedaluwarsa atau sudah disimpan. Silakan upload ulang file.";
                return RedirectToAction(nameof(Import));
            }
            if (result.HasErrors)
            {
                TempData["Error"] = "Masih ada baris error. Perbaiki file lalu upload ulang.";
                return RedirectToAction(nameof(Import));
            }

            var rows = result.Rows.Where(r => r.AdaPerubahan).ToList();
            if (rows.Count == 0)
            {
                _cache.Remove(CacheKey(token));
                TempData["Error"] = "Tidak ada perubahan stok di file (kolom Jumlah kosong semua).";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                var s = await _repo.ApplyStockImportAsync(rows, result.FileName, User.AdminId(), User.AdminName());
                _cache.Remove(CacheKey(token));
                var parts = new List<string>();
                if (s.Masuk > 0) parts.Add($"{s.Masuk} stok masuk");
                if (s.Opname > 0) parts.Add($"{s.Opname} stok opname");
                if (s.ObatBaru > 0) parts.Add($"{s.ObatBaru} obat baru");
                if (s.StokMinimum > 0) parts.Add($"{s.StokMinimum} stok minimum diubah");
                TempData["Success"] = $"Import stok dari {result.FileName} berhasil: {string.Join(", ", parts)}. Semua tercatat di kartu stok.";
                return RedirectToAction(nameof(Index));
            }
            catch (StockException ex)
            {
                TempData["Error"] = "Import dibatalkan, tidak ada stok yang berubah. " + ex.Message;
                return RedirectToAction(nameof(Import));
            }
        }

        private string CacheKey(string token) => $"stock-import:{User.AdminId()}:{token}";

        // Kartu stok: riwayat mutasi satu obat
        [HttpGet("Card/{id:int}")]
        public async Task<IActionResult> Card(int id)
        {
            var med = await _repo.GetMedicineByIdAsync(id);
            if (med is null)
                return NotFound();
            ViewBag.Movements = await _repo.GetStockCardAsync(id);
            return View(med);
        }
    }
}
