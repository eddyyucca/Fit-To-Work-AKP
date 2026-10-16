using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>Hasil baca + penilaian file import pemkes, disimpan sementara sampai admin menekan Simpan.</summary>
    public class CheckupImportPreview
    {
        public string Token { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public List<CheckupImportRow> Rows { get; set; } = new();
        public List<string> FileErrors { get; set; } = new();
        public List<string> FileWarnings { get; set; } = new();
        public List<CheckupImportItem> Items { get; set; } = new();   // yang siap disimpan

        public int Siap => Rows.Count(r => r.Status == CheckupImportStatus.Siap);
        public int Dilewati => Rows.Count(r => r.Status == CheckupImportStatus.Dilewati);
        public int Gagal => Rows.Count(r => r.Status == CheckupImportStatus.Gagal);
    }

    // Import hasil pemeriksaan kesehatan dari Excel.
    // Alur: unduh template (kolom dari menu Parameter & Skor Pemkes) -> isi -> upload -> pratinjau (skor dihitung) -> Simpan.
    // Karyawan dicocokkan dengan NIP; umur & jenis kelamin dari Data Karyawan. Hak akses sama dengan input pemkes (medical.checkup).
    public partial class CheckupsController
    {
        private static readonly TimeSpan ImportPreviewLifetime = TimeSpan.FromMinutes(30);

        [HttpGet("Import")]
        public async Task<IActionResult> Import()
        {
            await LoadFormAsync();
            return View();
        }

        [HttpGet("Import/Template")]
        public async Task<IActionResult> ImportTemplate()
        {
            var parameters = await _parameterRepository.GetAllAsync(onlyActive: true);
            var riskLevels = await _parameterRepository.GetRiskLevelsAsync();
            var bytes = CheckupImport.BuildTemplate(parameters, riskLevels, _clock.LocalNow);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Template_Import_Pemkes_{_clock.Today:yyyyMMdd}.xlsx");
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

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var preview = await BuildImportPreviewAsync(stream, Path.GetFileName(file.FileName));
            preview.Token = Guid.NewGuid().ToString("N");
            if (preview.FileErrors.Count == 0 && preview.Items.Count > 0)
                _cache.Set(ImportCacheKey(preview.Token), preview, ImportPreviewLifetime);
            return View("ImportPreview", preview);
        }

        [HttpPost("Import/Confirm")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportConfirm(string token)
        {
            var key = ImportCacheKey(token ?? string.Empty);
            if (string.IsNullOrEmpty(token) || !_cache.TryGetValue(key, out CheckupImportPreview? preview) || preview is null)
            {
                TempData["Error"] = "Pratinjau import sudah kedaluwarsa atau sudah disimpan. Silakan upload ulang file.";
                return RedirectToAction(nameof(Import));
            }
            _cache.Remove(key);   // sekali pakai: klik Simpan 2x / refresh tidak menyimpan dobel

            List<int> skippedNow;
            try
            {
                skippedNow = await _repo.CreateManyAsync(preview.Items);
            }
            catch (Exception)
            {
                TempData["Error"] = "Import dibatalkan karena kesalahan database, tidak ada pemeriksaan yang tersimpan. Silakan upload ulang file.";
                return RedirectToAction(nameof(Import));
            }

            var saved = preview.Items.Count - skippedNow.Count;
            var dilewati = preview.Dilewati + skippedNow.Count;
            var parts = new List<string> { $"{saved} pemeriksaan tersimpan" };
            if (dilewati > 0) parts.Add($"{dilewati} dilewati (sudah ada pemkes di tanggal yang sama)");
            if (preview.Gagal > 0) parts.Add($"{preview.Gagal} baris gagal tidak disimpan");
            var followUps = preview.Items.Count(i => i.Checkup.AdaTemuan && !skippedNow.Contains(i.RowNumber));
            if (followUps > 0) parts.Add($"{followUps} follow up dijadwalkan");
            TempData["Success"] = $"Import pemkes dari {preview.FileName}: {string.Join(", ", parts)}.";
            return RedirectToAction(nameof(Index));
        }

        private string ImportCacheKey(string token) => $"checkup-import:{User.AdminId()}:{token}";

        /// <summary>Baca file, cocokkan NIP, cek dobel, hitung skor. Belum ada yang disimpan.</summary>
        private async Task<CheckupImportPreview> BuildImportPreviewAsync(Stream stream, string fileName)
        {
            var parameters = await _parameterRepository.GetAllAsync(onlyActive: true);
            var riskLevels = await _parameterRepository.GetRiskLevelsAsync();
            var file = CheckupImport.Read(stream, fileName, parameters);
            var preview = new CheckupImportPreview
            {
                FileName = fileName,
                Rows = file.Rows,
                FileErrors = file.FileErrors,
                FileWarnings = file.FileWarnings
            };
            if (file.FileErrors.Count > 0)
                return preview;

            // 1. Cocokkan NIP
            var nips = file.Rows.Where(r => r.Nip.Length > 0).Select(r => r.Nip).ToList();
            var employees = (await _employeeRepository.GetByNIPsAsync(nips))
                .GroupBy(e => e.NIP, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var nowLocal = _clock.LocalNow;
            var firstRowOf = new Dictionary<(int, DateTime), int>();
            var matched = new List<(CheckupImportRow Row, Employee Emp)>();

            foreach (var row in file.Rows)
            {
                if (row.Nip.Length == 0)
                    continue;
                if (!employees.TryGetValue(row.Nip, out var emp))
                {
                    row.Errors.Insert(0, $"NIP {row.Nip} tidak ditemukan di Data Karyawan");
                    continue;
                }
                row.EmployeeId = emp.Id;
                row.Nama = emp.Nama;
                row.Department = emp.DepartmentName;

                if (emp.IsMitra)
                {
                    row.Errors.Insert(0, "Karyawan mitra hanya untuk kunjungan klinik, bukan pemeriksaan kesehatan");
                    continue;
                }
                if (!emp.IsActive)
                    row.Warnings.Add("Karyawan berstatus nonaktif");
                if (row.NamaDiFile is not null && CheckupImport.HeaderKey(row.NamaDiFile) != CheckupImport.HeaderKey(emp.Nama))
                    row.Warnings.Add($"Nama di file \"{row.NamaDiFile}\" berbeda dengan Data Karyawan — dipakai \"{emp.Nama}\", cek NIP-nya");

                if (row.ExaminedAtLocal is { } at)
                {
                    if (at > nowLocal.AddMinutes(10))
                        row.Errors.Add("Tanggal pemeriksaan tidak boleh di masa depan");
                    else if (firstRowOf.TryGetValue((emp.Id, at.Date), out var first))
                        row.Errors.Add($"Karyawan & tanggal yang sama dengan baris {first} di file ini");
                    else
                        firstRowOf[(emp.Id, at.Date)] = row.RowNumber;
                }
                matched.Add((row, emp));
            }

            // 2. Sudah ada pemkes di tanggal yang sama? -> dilewati
            var dated = matched.Where(m => m.Row.ExaminedAtLocal.HasValue).ToList();
            var existingDays = new HashSet<(int, DateTime)>();
            if (dated.Count > 0)
            {
                var fromUtc = _clock.ToUtc(dated.Min(m => m.Row.ExaminedAtLocal!.Value.Date));
                var toUtc = _clock.ToUtc(dated.Max(m => m.Row.ExaminedAtLocal!.Value.Date).AddDays(1));
                var times = await _repo.GetExamTimesAsync(dated.Select(m => m.Emp.Id).Distinct().ToList(), fromUtc, toUtc);
                foreach (var t in times)
                    existingDays.Add((t.EmployeeId, _clock.ToLocal(t.ExaminedAt).Date));
            }

            // 3. Nilai & skor — logika yang sama dengan form input
            foreach (var (row, emp) in matched)
            {
                if (row.ExaminedAtLocal is not { } at)
                    continue;

                var score = CheckupScoring.Evaluate(parameters, riskLevels, new CheckupScoreInput
                {
                    Values = row.Values,
                    BeratBadan = row.BeratBadan,
                    TinggiBadan = row.TinggiBadan,
                    TanggalLahir = emp.TanggalLahir,
                    JenisKelamin = emp.JenisKelamin,
                    TanggalPemeriksaan = at.Date
                });
                row.Errors.AddRange(score.Errors);
                row.Warnings.AddRange(score.Warnings.Select(w => w + " (lengkapi di Data Karyawan)"));

                var adaIsian = score.Values.Any(v => v.InputType is not (CheckupInputTypes.Age or CheckupInputTypes.Sex))
                    || !string.IsNullOrWhiteSpace(row.Keluhan);
                if (!adaIsian)
                    row.Errors.Add("Belum ada hasil pemeriksaan atau keluhan yang diisi");

                if (row.AdaTemuan && row.FollowUpDate is { } fu)
                {
                    if (fu < at.Date)
                        row.Errors.Add("Tanggal follow up tidak boleh sebelum tanggal pemeriksaan");
                    else if (fu > at.Date.AddYears(2))
                        row.Errors.Add("Tanggal follow up maksimal 2 tahun setelah pemeriksaan");
                }

                row.TotalSkor = score.ScoredFilled > 0 ? score.Total : null;
                row.RisikoLabel = score.Risk?.Label;
                row.RisikoColor = score.Risk?.Color;
                row.Abnormal = score.Values
                    .Where(v => CheckupLevels.IsAbnormal(v.Level))
                    .Select(v => $"{v.ParameterName}: {(v.NumericValue.HasValue ? CheckupScoring.Fmt(v.NumericValue) : v.TextValue)}")
                    .ToList();

                if (row.Errors.Count > 0)
                    continue;

                if (existingDays.Contains((emp.Id, at.Date)))
                {
                    row.Status = CheckupImportStatus.Dilewati;
                    row.Warnings.Insert(0, $"Sudah ada pemkes {emp.Nama} tanggal {at:dd/MM/yyyy} — dilewati");
                    continue;
                }

                preview.Items.Add(new CheckupImportItem
                {
                    RowNumber = row.RowNumber,
                    DayStartUtc = _clock.ToUtc(at.Date),
                    DayEndUtc = _clock.ToUtc(at.Date.AddDays(1)),
                    Checkup = new HealthCheckup
                    {
                        EmployeeId = emp.Id,
                        NamaSnapshot = emp.Nama,
                        DepartmentSnapshot = emp.DepartmentName ?? string.Empty,
                        SectionSnapshot = emp.SectionName ?? string.Empty,
                        PositionSnapshot = emp.PositionName ?? string.Empty,
                        JenisKelamin = emp.JenisKelamin,
                        ExaminedAt = _clock.ToUtc(at),
                        BeratBadan = row.BeratBadan,
                        TinggiBadan = row.TinggiBadan,
                        Keluhan = row.Keluhan,
                        Catatan = row.Catatan,
                        AdaTemuan = row.AdaTemuan,
                        TemuanKeterangan = row.AdaTemuan ? row.Temuan : null,
                        FollowUpDate = row.AdaTemuan ? row.FollowUpDate : null,
                        UsesParameters = true,
                        Values = score.Values,
                        TotalSkor = row.TotalSkor,
                        SkorParamTerisi = score.ScoredFilled,
                        SkorParamTotal = score.ScoredTotal,
                        RisikoLabel = score.Risk?.Label,
                        RisikoColor = score.Risk?.Color,
                        ExaminerAdminId = User.AdminId(),
                        ExaminerName = User.AdminName()
                    }
                });
            }

            foreach (var row in file.Rows.Where(r => r.Errors.Count > 0))
                row.Status = CheckupImportStatus.Gagal;
            return preview;
        }
    }
}
