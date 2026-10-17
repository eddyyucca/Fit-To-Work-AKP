using ClosedXML.Excel;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Laporan Klinik: analisis kunjungan, penyakit, obat, pemeriksaan kesehatan, dan tes urin/alkohol.
    /// HANYA MEMBACA data yang sudah ada; tidak mengubah data, dashboard, maupun laporan lain.
    /// Tiap bagian mengikuti permission aslinya: kunjungan & obat = medical.visit, pemkes = medical.checkup,
    /// tes urin & alkohol = medical.drugtest.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.MedicalVisit, Permissions.MedicalCheckup, Permissions.MedicalDrugTest)]
    [Route("Admin/ClinicReports")]
    public class ClinicReportsController : Controller
    {
        private const int MaxRangeDays = 366;
        private const int TopDiseaseCount = 15;
        private const int TopMedicineCount = 15;
        private const int TrendDiseaseCount = 5;
        private const int RepeatMinVisits = 3;
        private const int RepeatTop = 50;
        private const int RepeatDiseaseDays = 7;
        private const int StockAlertDays = 14;
        private const int StockAlertMax = 40;
        private const int FollowUpTop = 30;

        private static readonly string[] BulanId =
            { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };

        private readonly IClinicReportRepository _repo;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IPermissionService _permissions;
        private readonly IAppClock _clock;

        public ClinicReportsController(IClinicReportRepository repo, IOrganizationRepository orgRepository,
            IPermissionService permissions, IAppClock clock)
        {
            _repo = repo;
            _orgRepository = orgRepository;
            _permissions = permissions;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department)
        {
            return View(await BuildAsync(from, to, department));
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department)
        {
            var vm = await BuildAsync(from, to, department);

            using var wb = new XLWorkbook();
            WriteSummarySheet(wb.Worksheets.Add("Ringkasan"), vm);

            if (vm.CanVisit)
            {
                var ws = wb.Worksheets.Add("Per Department");
                var r = Header(ws, vm, "Kunjungan per Department");
                WriteTable(ws, r, "Kunjungan per department", new[] { "Department", "Kunjungan", "Karyawan unik", "Karyawan aktif", "Per 100 karyawan", "Hari istirahat", "Rujuk" },
                    vm.ByDepartment, d => d.Department, d => d.Jumlah, d => d.Karyawan, d => d.KaryawanAktif, d => d.PerSeratus, d => d.HariIstirahat, d => d.Rujuk);

                ws = wb.Worksheets.Add("Penyakit");
                r = Header(ws, vm, "Penyakit");
                r = WriteTable(ws, r, "Per kategori penyakit", new[] { "Kategori", "Kasus", "Karyawan unik" }, vm.ByCategory, c => c.Nama, c => c.Jumlah, c => c.Karyawan);
                r = WriteTable(ws, r, $"{TopDiseaseCount} penyakit terbanyak", new[] { "Kode", "Penyakit", "Kategori", "Kasus", "Karyawan unik" },
                    vm.TopDiseases, d => d.Kode, d => d.Nama, d => d.Kategori, d => d.Jumlah, d => d.Karyawan);
                WriteTable(ws, r, $"Kasus berulang (penyakit sama dalam {RepeatDiseaseDays} hari)", new[] { "Penyakit", "Kasus berulang", "Karyawan unik" },
                    vm.RepeatDiseases, d => d.Nama, d => d.Jumlah, d => d.Karyawan);

                ws = wb.Worksheets.Add("Kunjungan Berulang");
                r = Header(ws, vm, $"Karyawan dengan {RepeatMinVisits} kunjungan atau lebih");
                WriteTable(ws, r, "Kunjungan berulang", new[] { "NIP", "Nama", "Department", "Kunjungan", "Hari istirahat", "Terakhir" },
                    vm.RepeatVisitors, v => v.NIP, v => v.Nama, v => v.Department, v => v.Jumlah, v => v.HariIstirahat, v => _clock.ToLocal(v.TerakhirUtc));

                ws = wb.Worksheets.Add("Profil & Jam Sibuk");
                r = Header(ws, vm, "Profil pasien dan jam sibuk");
                var byGender = vm.Profile.GroupBy(p => GenderLabel(p.JenisKelamin)).Select(g => (Nama: g.Key, Jumlah: g.Sum(x => x.Jumlah)));
                r = WriteTable(ws, r, "Per jenis kelamin", new[] { "Jenis kelamin", "Kunjungan" }, byGender, g => g.Nama, g => g.Jumlah);
                var byAge = AgeOrder.Select(a => (Nama: a, Jumlah: vm.Profile.Where(p => p.KelompokUmur == a).Sum(x => x.Jumlah))).Where(x => x.Jumlah > 0);
                r = WriteTable(ws, r, "Per kelompok umur", new[] { "Kelompok umur", "Kunjungan" }, byAge, g => g.Nama, g => g.Jumlah);
                WriteHeatmap(ws, r, vm);

                ws = wb.Worksheets.Add("Obat");
                r = Header(ws, vm, "Obat dan stok");
                r = WriteTable(ws, r, $"{TopMedicineCount} obat terbanyak keluar", new[] { "Obat", "Satuan", "Jumlah keluar", "Kunjungan" },
                    vm.TopMedicines, m => m.Nama, m => m.Satuan, m => m.Qty, m => m.Kunjungan);
                WriteTable(ws, r, $"Peringatan stok (di bawah minimum atau habis dalam {StockAlertDays} hari)", new[] { "Obat", "Satuan", "Stok", "Stok minimum", "Keluar 30 hari", "Per hari", "Perkiraan habis (hari)" },
                    vm.StockAlerts, s => s.Nama, s => s.Satuan, s => s.Stok, s => s.StokMinimum, s => s.Qty30Hari, s => s.PemakaianPerHari, s => s.PerkiraanHariHabis);
            }

            if (vm.CanCheckup)
            {
                var ws = wb.Worksheets.Add("Pemeriksaan Kesehatan");
                var r = Header(ws, vm, "Pemeriksaan kesehatan");
                r = WriteTable(ws, r, "Sebaran risiko", new[] { "Risiko", "Jumlah" }, vm.RiskDistribution, x => x.Label, x => x.Jumlah);
                r = WriteTable(ws, r, "Per department", new[] { "Department", "Pemeriksaan", "Rata sistolik", "Rata diastolik", "TD tinggi", "Gula tinggi", "Kolesterol tinggi" },
                    vm.CheckupByDepartment, d => d.Department, d => d.Total, d => d.RataSistolik, d => d.RataDiastolik, d => d.TdTinggi, d => d.GulaTinggi, d => d.KolesterolTinggi);
                WriteTable(ws, r, "Tindak lanjut terlambat (semua periode)", new[] { "NIP", "Nama", "Department", "Diperiksa", "Jatuh tempo", "Terlambat (hari)" },
                    vm.OverdueFollowUps, f => f.NIP, f => f.Nama, f => f.Department, f => _clock.ToLocal(f.ExaminedAtUtc), f => f.FollowUpDate, f => f.HariTerlambat);
            }

            if (vm.CanDrug)
            {
                var ws = wb.Worksheets.Add("Tes Urin & Alkohol");
                var r = Header(ws, vm, "Tes urin dan alkohol");
                r = WriteTable(ws, r, "Per alasan tes", new[] { "Alasan", "Tes", "Urin positif", "Alkohol positif" }, vm.DrugByReason, d => d.Nama, d => d.Total, d => d.UrinPositif, d => d.AlkoholPositif);
                WriteTable(ws, r, "Per department", new[] { "Department", "Tes", "Urin positif", "Alkohol positif" }, vm.DrugByDepartment, d => d.Nama, d => d.Total, d => d.UrinPositif, d => d.AlkoholPositif);
            }

            foreach (var ws in wb.Worksheets)
                ws.Columns().AdjustToContents(1, 200);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Laporan_Klinik_{vm.From:yyyyMMdd}_{vm.To:yyyyMMdd}.xlsx");
        }

        // ============================ Membangun data laporan ============================

        private static readonly string[] AgeOrder = { "< 25", "25-34", "35-44", "45-54", "55+", "Tidak diketahui" };

        private static string GenderLabel(string g) => g?.Trim() switch { "L" => "Laki-laki", "P" => "Perempuan", _ => "Tidak diketahui" };

        private async Task<ClinicReportViewModel> BuildAsync(DateTime? from, DateTime? to, string? department)
        {
            var access = await _permissions.GetAsync(User);
            var today = _clock.Today;

            var toD = (to ?? today).Date;
            var fromD = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
            if (fromD > toD) (fromD, toD) = (toD, fromD);
            var clamped = false;
            if ((toD - fromD).Days + 1 > MaxRangeDays)
            {
                fromD = toD.AddDays(-(MaxRangeDays - 1));
                clamped = true;
            }
            var days = (toD - fromD).Days + 1;
            var prevTo = fromD.AddDays(-1);
            var prevFrom = prevTo.AddDays(-(days - 1));

            var dept = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
            var offset = _clock.UtcOffsetMinutes;
            var cur = new ReportFilter(_clock.ToUtc(fromD), _clock.ToUtc(toD.AddDays(1)), dept, offset);
            var prev = new ReportFilter(_clock.ToUtc(prevFrom), _clock.ToUtc(prevTo.AddDays(1)), dept, offset);

            var departments = (await _orgRepository.GetDepartmentsAsync(onlyActive: true)).Select(d => d.Name).ToList();
            if (dept is not null && !departments.Contains(dept, StringComparer.OrdinalIgnoreCase))
                departments.Add(dept);

            var vm = new ClinicReportViewModel
            {
                From = fromD,
                To = toD,
                PrevFrom = prevFrom,
                PrevTo = prevTo,
                Days = days,
                Department = dept,
                Departments = departments,
                GeneratedAt = _clock.LocalNow,
                RangeDayLimit = MaxRangeDays,
                RangeWasClamped = clamped,
                CanVisit = access.Has(Permissions.MedicalVisit),
                CanCheckup = access.Has(Permissions.MedicalCheckup),
                CanDrug = access.Has(Permissions.MedicalDrugTest)
            };

            if (vm.CanVisit)
            {
                vm.Summary = await _repo.GetSummaryAsync(cur);
                vm.Previous = await _repo.GetSummaryAsync(prev);

                var daily = (await _repo.GetDailyTrendAsync(cur, fromD)).ToDictionary(x => x.Bucket, x => x.Jumlah);
                for (var i = 0; i < days; i++)
                {
                    vm.TrendLabels.Add(fromD.AddDays(i).ToString("dd/MM"));
                    vm.TrendValues.Add(daily.TryGetValue(i, out var n) ? n : 0);
                }

                vm.ByDepartment = await _repo.GetByDepartmentAsync(cur);
                vm.ByCategory = await _repo.GetByCategoryAsync(cur);
                vm.TopDiseases = await _repo.GetTopDiseasesAsync(cur, TopDiseaseCount);
                BuildDiseaseTrend(vm, await _repo.GetDiseaseMonthlyAsync(cur, TrendDiseaseCount), fromD, toD);
                vm.RepeatVisitors = await _repo.GetRepeatVisitorsAsync(cur, RepeatMinVisits, RepeatTop);
                vm.RepeatDiseases = await _repo.GetRepeatDiseasesAsync(cur, RepeatDiseaseDays);
                vm.Profile = await _repo.GetProfileAsync(cur);
                foreach (var h in await _repo.GetHeatmapAsync(cur))
                    if (h.Hari is >= 0 and <= 6 && h.Jam is >= 0 and <= 23)
                        vm.Heat[h.Hari, h.Jam] = h.Jumlah;
                vm.TopMedicines = await _repo.GetTopMedicinesAsync(cur, TopMedicineCount);
                vm.StockAlerts = (await _repo.GetStockAsync())
                    .Where(s => s.DiBawahMinimum || s.PerkiraanHariHabis is <= StockAlertDays)
                    .OrderByDescending(s => s.DiBawahMinimum)
                    .ThenBy(s => s.PerkiraanHariHabis ?? int.MaxValue)
                    .ThenBy(s => s.Nama)
                    .Take(StockAlertMax)
                    .ToList();
            }

            if (vm.CanCheckup)
            {
                vm.Checkup = await _repo.GetCheckupSummaryAsync(cur, today);
                vm.RiskDistribution = await _repo.GetRiskDistributionAsync(cur);
                vm.CheckupByDepartment = await _repo.GetCheckupByDepartmentAsync(cur);
                vm.OverdueFollowUps = await _repo.GetOverdueFollowUpsAsync(cur, today, FollowUpTop);
            }

            if (vm.CanDrug)
            {
                vm.Drug = await _repo.GetDrugSummaryAsync(cur);
                vm.DrugByReason = await _repo.GetDrugByReasonAsync(cur);
                vm.DrugByDepartment = await _repo.GetDrugByDepartmentAsync(cur);
            }

            return vm;
        }

        private static void BuildDiseaseTrend(ClinicReportViewModel vm, List<ReportDiseaseMonthRow> rows, DateTime fromD, DateTime toD)
        {
            var months = new List<DateTime>();
            for (var m = new DateTime(fromD.Year, fromD.Month, 1); m <= toD && months.Count < 60; m = m.AddMonths(1))
                months.Add(m);
            vm.DiseaseMonthLabels = months.Select(m => $"{BulanId[m.Month - 1]} {m:yyyy}").ToList();
            vm.DiseaseMonthSeries = rows
                .GroupBy(r => r.Nama)
                .Select(g => (Nama: g.Key,
                              Values: months.Select(m => g.Where(r => r.Bulan.Year == m.Year && r.Bulan.Month == m.Month).Sum(r => r.Jumlah)).ToList()))
                .OrderByDescending(s => s.Values.Sum())
                .ToList();
        }

        // ============================ Penulisan Excel ============================

        private static void SetCell(IXLCell cell, object? v)
        {
            switch (v)
            {
                case null: break;
                case string s: cell.Value = s; break;
                case int i: cell.Value = i; break;
                case long l: cell.Value = l; break;
                case decimal d: cell.Value = d; break;
                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm";
                    break;
                case bool b: cell.Value = b; break;
                default: cell.Value = v.ToString() ?? string.Empty; break;
            }
        }

        private int Header(IXLWorksheet ws, ClinicReportViewModel vm, string title)
        {
            ws.Cell(1, 1).Value = title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy} ({vm.Days} hari)" +
                                  (vm.Department is null ? "" : $" · Department: {vm.Department}");
            ws.Cell(3, 1).Value = $"Dibuat {vm.GeneratedAt:dd/MM/yyyy HH:mm}";
            return 5;
        }

        /// <summary>Menulis satu tabel (judul, header, baris). Mengembalikan nomor baris kosong berikutnya.</summary>
        private static int WriteTable<T>(IXLWorksheet ws, int row, string title, string[] headers, IEnumerable<T> rows, params Func<T, object?>[] cols)
        {
            ws.Cell(row, 1).Value = title;
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            for (var i = 0; i < headers.Length; i++)
                ws.Cell(row, i + 1).Value = headers[i];
            var head = ws.Range(row, 1, row, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            row++;

            var any = false;
            foreach (var item in rows)
            {
                any = true;
                for (var i = 0; i < cols.Length; i++)
                    SetCell(ws.Cell(row, i + 1), cols[i](item));
                row++;
            }
            if (!any)
            {
                ws.Cell(row, 1).Value = "(tidak ada data)";
                ws.Cell(row, 1).Style.Font.Italic = true;
                row++;
            }
            return row + 1;
        }

        private void WriteSummarySheet(IXLWorksheet ws, ClinicReportViewModel vm)
        {
            var r = Header(ws, vm, "Laporan Klinik - Ringkasan");
            ws.Cell(4, 1).Value = $"Pembanding: {vm.PrevFrom:dd/MM/yyyy} - {vm.PrevTo:dd/MM/yyyy}";

            if (vm.CanVisit)
            {
                var items = new (string Nama, int Ini, int Lalu)[]
                {
                    ("Kunjungan klinik", vm.Summary.TotalKunjungan, vm.Previous.TotalKunjungan),
                    ("Karyawan unik", vm.Summary.KaryawanUnik, vm.Previous.KaryawanUnik),
                    ("Kembali bekerja", vm.Summary.KembaliBekerja, vm.Previous.KembaliBekerja),
                    ("Kerja terbatas", vm.Summary.KerjaTerbatas, vm.Previous.KerjaTerbatas),
                    ("Istirahat", vm.Summary.Istirahat, vm.Previous.Istirahat),
                    ("Rujuk", vm.Summary.Rujuk, vm.Previous.Rujuk),
                    ("Total hari istirahat", vm.Summary.TotalHariIstirahat, vm.Previous.TotalHariIstirahat),
                    ("Obat keluar", vm.Summary.ObatKeluar, vm.Previous.ObatKeluar)
                };
                r = WriteTable(ws, r, "Kunjungan klinik", new[] { "Indikator", "Periode ini", "Periode sebelumnya", "Selisih" },
                    items, x => x.Nama, x => x.Ini, x => x.Lalu, x => x.Ini - x.Lalu);
            }

            if (vm.CanCheckup)
            {
                var c = vm.Checkup;
                var items = new (string Nama, object? Nilai)[]
                {
                    ("Pemeriksaan kesehatan", c.Total), ("Karyawan diperiksa", c.Karyawan), ("Karyawan aktif", c.KaryawanAktif),
                    ("Cakupan (%)", c.CakupanPersen), ("Ada temuan", c.AdaTemuan),
                    ("Tindak lanjut belum selesai (semua periode)", c.FollowUpBelumSelesai), ("Tindak lanjut terlambat (semua periode)", c.FollowUpTerlambat)
                };
                r = WriteTable(ws, r, "Pemeriksaan kesehatan", new[] { "Indikator", "Nilai" }, items, x => x.Nama, x => x.Nilai);
            }

            if (vm.CanDrug)
            {
                var d = vm.Drug;
                var items = new (string Nama, int Nilai)[]
                {
                    ("Total tes", d.Total), ("Tes urin", d.TesUrin), ("Urin positif", d.UrinPositif), ("Tes alkohol", d.TesAlkohol), ("Alkohol positif", d.AlkoholPositif)
                };
                WriteTable(ws, r, "Tes urin dan alkohol", new[] { "Indikator", "Nilai" }, items, x => x.Nama, x => x.Nilai);
            }
        }

        private static void WriteHeatmap(IXLWorksheet ws, int row, ClinicReportViewModel vm)
        {
            string[] hari = { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };
            ws.Cell(row, 1).Value = "Jam sibuk (kunjungan per hari dan jam)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            ws.Cell(row, 1).Value = "Hari";
            for (var h = 0; h < 24; h++) ws.Cell(row, h + 2).Value = $"{h:00}";
            var head = ws.Range(row, 1, row, 25);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            row++;
            for (var d = 0; d < 7; d++, row++)
            {
                ws.Cell(row, 1).Value = hari[d];
                for (var h = 0; h < 24; h++) ws.Cell(row, h + 2).Value = vm.Heat[d, h];
            }
        }
    }
}
