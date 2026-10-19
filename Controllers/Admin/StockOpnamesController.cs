using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FitToWorkApp.Controllers.Admin
{
    public class OpnameCountRow
    {
        public int ItemId { get; set; }
        public int? StokFisik { get; set; }
        public string? Catatan { get; set; }
    }

    public class OpnameCreateViewModel
    {
        [Required(ErrorMessage = "Label periode wajib diisi")]
        [StringLength(100)]
        public string? PeriodeLabel { get; set; }

        [Required(ErrorMessage = "Tanggal mulai periode wajib diisi")]
        public DateTime? PeriodeMulai { get; set; }

        [Required(ErrorMessage = "Tanggal selesai periode wajib diisi")]
        public DateTime? PeriodeSelesai { get; set; }

        [StringLength(500)]
        public string? Catatan { get; set; }

        public string Scope { get; set; } = "all";               // all = semua obat aktif; pilih = hanya yang dicentang
        public List<int> MedicineIds { get; set; } = new();
    }

    /// <summary>
    /// Stok opname obat per periode, bisa PARSIAL: hitungan disimpan bertahap, tiap obat mencatat tanggal & pengisi terakhir.
    /// Stok baru berubah saat selisih DITERAPKAN (per obat / sekaligus), tercatat di kartu stok. Permission: medical.master.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalMaster)]
    [Route("Admin/StockOpnames")]
    public class StockOpnamesController : Controller
    {
        private static readonly string[] BulanId =
            { "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };

        private const int OverdueDays = 35;

        private readonly IStockOpnameRepository _repo;
        private readonly IMedicalMasterRepository _masterRepo;
        private readonly IAppClock _clock;

        public StockOpnamesController(IStockOpnameRepository repo, IMedicalMasterRepository masterRepo, IAppClock clock)
        {
            _repo = repo;
            _masterRepo = masterRepo;
            _clock = clock;
        }

        // Migrasi (dbo.StockOpnames) belum dijalankan: tampilkan penjelasan, bukan halaman error
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!await _repo.IsAvailableAsync())
            {
                context.Result = View("NotReady");
                return;
            }
            await next();
        }

        // ============================ Daftar ============================

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Obat aktif yang belum pernah diopname, atau terakhir dihitung lebih dari OverdueDays hari lalu
            var last = await _repo.GetLastOpnameByMedicineAsync();
            var active = await _masterRepo.GetMedicinesAsync(onlyActive: true);
            var limit = DateTime.UtcNow.AddDays(-OverdueDays);
            ViewBag.NeverCounted = active.Count(m => !last.ContainsKey(m.Id));
            ViewBag.Overdue = active.Count(m => last.TryGetValue(m.Id, out var l) && l.CountedAt < limit);
            ViewBag.OverdueDays = OverdueDays;
            return View(await _repo.GetListAsync());
        }

        // ============================ Buat opname (pilih periode & obat) ============================

        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            var today = _clock.Today;
            var model = new OpnameCreateViewModel
            {
                PeriodeLabel = $"{BulanId[today.Month - 1]} {today.Year}",
                PeriodeMulai = new DateTime(today.Year, today.Month, 1),
                PeriodeSelesai = new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)
            };
            await LoadMedicinesAsync();
            return View(model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OpnameCreateViewModel model)
        {
            if (model.PeriodeMulai.HasValue && model.PeriodeSelesai.HasValue && model.PeriodeSelesai.Value.Date < model.PeriodeMulai.Value.Date)
                ModelState.AddModelError(nameof(model.PeriodeSelesai), "Tanggal selesai tidak boleh sebelum tanggal mulai");

            var active = await _masterRepo.GetMedicinesAsync(onlyActive: true);
            var ids = model.Scope == "pilih"
                ? model.MedicineIds.Distinct().Where(id => active.Any(m => m.Id == id)).ToList()
                : active.Select(m => m.Id).ToList();
            if (ids.Count == 0)
                ModelState.AddModelError(nameof(model.MedicineIds), model.Scope == "pilih" ? "Pilih minimal satu obat" : "Belum ada obat aktif");

            if (ModelState.IsValid)
            {
                try
                {
                    var id = await _repo.CreateAsync(new StockOpname
                    {
                        PeriodeLabel = model.PeriodeLabel!.Trim(),
                        PeriodeMulai = model.PeriodeMulai!.Value.Date,
                        PeriodeSelesai = model.PeriodeSelesai!.Value.Date,
                        Catatan = string.IsNullOrWhiteSpace(model.Catatan) ? null : model.Catatan.Trim(),
                        CreatedBy = User.AdminName()
                    }, ids);
                    TempData["Success"] = $"Stok opname {model.PeriodeLabel!.Trim()} dibuat dengan {ids.Count} obat. Isi stok fisik bertahap; tiap simpan boleh sebagian.";
                    return RedirectToAction(nameof(Detail), new { id });
                }
                catch (StockException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }

            await LoadMedicinesAsync();
            return View(model);
        }

        // ============================ Isi hitungan ============================

        // show: semua | belum | dihitung (sudah dihitung, belum diterapkan) | diterapkan
        [HttpGet("Detail/{id:int}")]
        public async Task<IActionResult> Detail(int id, string? show)
        {
            var o = await _repo.GetByIdAsync(id);
            if (o is null) return NotFound();

            var filter = (show ?? "semua").ToLowerInvariant();
            var items = await _repo.GetItemsAsync(id);
            ViewBag.Show = filter is "belum" or "dihitung" or "diterapkan" ? filter : "semua";
            ViewBag.Items = items.Where(i => (string)ViewBag.Show switch
            {
                "belum" => !i.IsCounted,
                "dihitung" => i.IsCounted && !i.IsApplied,
                "diterapkan" => i.IsApplied,
                _ => true
            }).ToList();
            ViewBag.AllItems = items;
            ViewBag.NotIncluded = o.IsOpen ? await _repo.GetMedicinesNotInAsync(id) : new List<Medicine>();
            return View(o);
        }

        // Simpan hitungan (parsial: baris kosong dilewati; tanggal pengisian baris yang tidak berubah dipertahankan)
        [HttpPost("Save/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(int id, string? show, List<OpnameCountRow> counts)
        {
            try
            {
                var n = await _repo.SaveCountsAsync(id, (counts ?? new()).Select(c => new OpnameCountInput(c.ItemId, c.StokFisik, c.Catatan)), User.AdminName());
                TempData["Success"] = n == 0 ? "Tidak ada perubahan hitungan." : $"Hitungan {n} obat disimpan. Sisanya boleh dilanjutkan nanti.";
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id, show });
        }

        // Terapkan selisih ke stok: yang dicentang, atau semua yang sudah dihitung. Hitungan yang baru diketik ikut disimpan dulu.
        [HttpPost("Apply/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(int id, string? show, string mode, List<int>? applyIds, List<OpnameCountRow> counts)
        {
            try
            {
                if (counts is { Count: > 0 })
                    await _repo.SaveCountsAsync(id, counts.Select(c => new OpnameCountInput(c.ItemId, c.StokFisik, c.Catatan)), User.AdminName());

                if (mode == "selected" && (applyIds is null || applyIds.Count == 0))
                {
                    TempData["Error"] = "Centang obat yang selisihnya akan diterapkan.";
                    return RedirectToAction(nameof(Detail), new { id, show });
                }

                var r = await _repo.ApplyAsync(id, mode == "selected" ? applyIds : null, User.AdminId(), User.AdminName());
                if (r.Diterapkan > 0)
                    TempData["Success"] = $"{r.Diterapkan} obat diterapkan: {r.Berubah} stok berubah (selisih bersih {r.TotalSelisih:+#;-#;0}), sisanya sudah sesuai. Tercatat di kartu stok.";
                if (r.Gagal.Count > 0)
                    TempData["Error"] = "Tidak bisa diterapkan: " + string.Join("; ", r.Gagal);
                if (r.Diterapkan == 0 && r.Gagal.Count == 0)
                    TempData["Error"] = "Tidak ada hitungan yang siap diterapkan. Isi stok fisik dulu.";
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id, show });
        }

        [HttpPost("AddMedicines/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMedicines(int id, List<int>? medicineIds)
        {
            try
            {
                var n = await _repo.AddMedicinesAsync(id, medicineIds ?? new());
                TempData["Success"] = n == 0 ? "Tidak ada obat yang ditambahkan." : $"{n} obat ditambahkan ke opname ini.";
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpPost("Finish/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finish(int id)
        {
            try
            {
                await _repo.FinishAsync(id, User.AdminName());
                TempData["Success"] = "Stok opname diselesaikan. Obat yang belum dihitung ditandai di laporan.";
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpPost("Cancel/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            try
            {
                await _repo.CancelAsync(id);
                TempData["Success"] = "Stok opname dibatalkan.";
                return RedirectToAction(nameof(Index));
            }
            catch (StockException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Detail), new { id });
            }
        }

        // ============================ Excel ============================

        [HttpGet("Export/{id:int}")]
        public async Task<IActionResult> Export(int id)
        {
            var o = await _repo.GetByIdAsync(id);
            if (o is null) return NotFound();
            var items = await _repo.GetItemsAsync(id);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Stok Opname");
            var r = ReportExcel.WriteTitle(ws, $"Stok Opname Obat - {o.PeriodeLabel}",
                $"Periode {o.PeriodeMulai:dd/MM/yyyy} - {o.PeriodeSelesai:dd/MM/yyyy} · Status {o.Status} · Dihitung {o.Dihitung}/{o.TotalItem} obat" +
                (o.LastFilledAt is DateTime lf ? $" · Terakhir diisi {_clock.ToLocal(lf):dd/MM/yyyy HH:mm}" : ""),
                $"Dibuat {_clock.LocalNow:dd/MM/yyyy HH:mm}");
            ReportExcel.WriteTable(ws, r, "Hasil hitungan",
                new[] { "Obat", "Satuan", "Stok sistem (saat dihitung)", "Stok fisik", "Selisih", "Tanggal hitung", "Dihitung oleh", "Status", "Diterapkan", "Catatan" },
                items,
                i => i.NamaSnapshot, i => i.SatuanSnapshot, i => i.StokSistem, i => i.StokFisik, i => i.Selisih,
                i => i.CountedAt is DateTime c ? (object)_clock.ToLocal(c) : null, i => i.CountedBy,
                i => i.IsApplied ? "Diterapkan" : i.IsCounted ? "Dihitung, belum diterapkan" : "Belum dihitung",
                i => i.AppliedAt is DateTime a ? (object)_clock.ToLocal(a) : null, i => i.Catatan);
            ws.Columns().AdjustToContents(1, 200);
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), ReportExcel.ContentType, $"Stok_Opname_{o.PeriodeMulai:yyyyMMdd}_{o.PeriodeSelesai:yyyyMMdd}.xlsx");
        }

        private async Task LoadMedicinesAsync()
        {
            ViewBag.Medicines = await _masterRepo.GetMedicinesAsync(onlyActive: true);
            ViewBag.LastOpname = await _repo.GetLastOpnameByMedicineAsync();
        }
    }
}
