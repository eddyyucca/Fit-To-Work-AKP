using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Laporan Fit To Work: hasil pengisian, kepatuhan karyawan &amp; department, dan tindak lanjut karyawan yang tidak fit.
    /// HANYA MEMBACA data yang sudah ada (tidak mengubah data, dashboard, maupun riwayat). Permission: ftw.history.
    /// Kolom tindak lanjut dari klinik hanya ditampilkan kalau admin juga punya permission medical.visit.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.FtwHistory)]
    [Route("Admin/FtwReports")]
    public class FtwReportsController : Controller
    {
        private const int MaxRangeDays = 366;
        private const int LowComplianceShown = 100;
        private const int FrequentUnfitShown = 30;
        private const int FrequentUnfitMin = 3;
        private const int FollowUpMax = 1000;
        private const decimal TertibMin = 90m;
        private const decimal CukupMin = 70m;

        private static readonly string[] HariId = { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };

        private readonly IFtwReportRepository _repo;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IFormOptionRepository _formOptions;
        private readonly IPermissionService _permissions;
        private readonly IAppClock _clock;

        public FtwReportsController(IFtwReportRepository repo, IOrganizationRepository orgRepository,
            IFormOptionRepository formOptions, IPermissionService permissions, IAppClock clock)
        {
            _repo = repo;
            _orgRepository = orgRepository;
            _formOptions = formOptions;
            _permissions = permissions;
            _clock = clock;
        }

        // ============================ Laporan ============================

        [HttpGet("")]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department)
        {
            return View(await BuildReportAsync(from, to, department));
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department)
        {
            var vm = await BuildReportAsync(from, to, department);
            var subtitle = $"Periode {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy} ({vm.Days} hari)" + (vm.Department is null ? "" : $" · Department: {vm.Department}");
            var generated = $"Dibuat {vm.GeneratedAt:dd/MM/yyyy HH:mm}";

            using var wb = new XLWorkbook();

            var ws = wb.Worksheets.Add("Ringkasan");
            var r = ReportExcel.WriteTitle(ws, "Laporan Fit To Work - Ringkasan", subtitle, generated);
            var items = new (string Nama, object? Nilai)[]
            {
                ("Karyawan aktif (AKP)", vm.KaryawanAktif), ("Hari operasional", vm.HariOperasional), ("Total pengisian (karyawan-hari)", vm.TotalPengisian),
                ("Fit", vm.TotalFit), ("Tidak fit", vm.TotalTidakFit), ("Kepatuhan rata-rata (%)", vm.KepatuhanPersen),
                ("Karyawan tertib (>= 90%)", vm.JumlahTertib), ("Karyawan cukup (70-89%)", vm.JumlahCukup),
                ("Karyawan kurang (< 70%)", vm.JumlahKurang), ("Karyawan tidak pernah mengisi", vm.JumlahTidakPernah)
            };
            r = ReportExcel.WriteTable(ws, r, "Ringkasan", new[] { "Indikator", "Nilai" }, items, x => x.Nama, x => x.Nilai);
            ReportExcel.WriteTable(ws, r, "Catatan",
                new[] { "Keterangan" },
                new[]
                {
                    "Kepatuhan = hari terisi / hari wajib. Hari wajib = hari operasional (tanggal yang ada minimal 1 pengisian) sejak karyawan terdaftar.",
                    "Cuti, libur individu, dan jadwal shift tidak diperhitungkan, jadi kepatuhan karyawan yang sedang tidak bekerja akan tampak lebih rendah.",
                    "1 karyawan dihitung 1x per hari; kalau mengisi ulang, status terakhir yang dipakai."
                }, x => x);

            ws = wb.Worksheets.Add("Harian");
            r = ReportExcel.WriteTitle(ws, "Pengisian per hari", subtitle, generated);
            ReportExcel.WriteTable(ws, r, "Per hari", new[] { "Tanggal", "Hari", "Target", "Sudah mengisi", "Kepatuhan (%)", "Fit", "Tidak fit" },
                vm.Daily, d => d.Tanggal, d => HariId[((int)d.Tanggal.DayOfWeek + 6) % 7], d => d.Target, d => d.Isi, d => d.Persen, d => d.Fit, d => d.TidakFit);

            string[] groupHead = { "Nama", "Karyawan", "Hari wajib", "Hari terisi", "Kepatuhan (%)", "Tidak pernah mengisi", "Kepatuhan < 70%", "Hari tidak fit", "Tidak fit (%)" };
            ws = wb.Worksheets.Add("Per Department");
            r = ReportExcel.WriteTitle(ws, "Kepatuhan per department (terendah dulu)", subtitle, generated);
            ReportExcel.WriteTable(ws, r, "Per department", groupHead, vm.ByDepartment,
                g => g.Nama, g => g.Karyawan, g => g.HariWajib, g => g.HariIsi, g => g.Persen, g => g.TidakPernah, g => g.Kurang, g => g.HariTidakFit, g => g.PersenTidakFit);

            ws = wb.Worksheets.Add("Per Section");
            r = ReportExcel.WriteTitle(ws, "Kepatuhan per section (terendah dulu)", subtitle, generated);
            ReportExcel.WriteTable(ws, r, "Per section", groupHead, vm.BySection,
                g => g.Nama, g => g.Karyawan, g => g.HariWajib, g => g.HariIsi, g => g.Persen, g => g.TidakPernah, g => g.Kurang, g => g.HariTidakFit, g => g.PersenTidakFit);

            ws = wb.Worksheets.Add("Karyawan");
            r = ReportExcel.WriteTitle(ws, "Ketertiban pengisian per karyawan", subtitle, generated);
            ws.Column(1).Style.NumberFormat.Format = "@";
            ReportExcel.WriteTable(ws, r, "Semua karyawan aktif (terendah dulu)",
                new[] { "NIP", "Nama", "Department", "Section", "Hari wajib", "Hari terisi", "Kepatuhan (%)", "Kategori", "Hari tidak fit", "Belum mengisi (hari operasional terakhir)", "Terakhir mengisi" },
                vm.Employees.OrderBy(e => e.Persen ?? 101m).ThenByDescending(e => e.BelumIsiBerturut).ThenBy(e => e.Nama),
                e => e.NIP, e => e.Nama, e => e.Department, e => e.Section, e => e.HariWajib, e => e.HariIsi, e => e.Persen, e => e.Kategori,
                e => e.HariTidakFit, e => e.BelumIsiBerturut, e => e.TerakhirUtc is DateTime t ? (object)_clock.ToLocal(t) : null);

            ws = wb.Worksheets.Add("Hasil Fit To Work");
            r = ReportExcel.WriteTitle(ws, "Hasil Fit To Work", subtitle, generated);
            r = ReportExcel.WriteTable(ws, r, "Per jadwal kerja", new[] { "Jadwal", "Fit", "Tidak fit", "Total", "Tidak fit (%)" }, vm.ByJadwal, x => x.Nama, x => x.Fit, x => x.TidakFit, x => x.Total, x => x.PersenTidakFit);
            r = ReportExcel.WriteTable(ws, r, "Per jam tidur", new[] { "Jam tidur", "Fit", "Tidak fit", "Total", "Tidak fit (%)" }, vm.ByJamTidur, x => x.Nama, x => x.Fit, x => x.TidakFit, x => x.Total, x => x.PersenTidakFit);
            r = ReportExcel.WriteTable(ws, r, "Gejala (yang tidak fit)", new[] { "Gejala", "Jumlah" }, vm.Gejala, x => x.Nama, x => x.Jumlah);
            r = ReportExcel.WriteTable(ws, r, "Kualitas tidur (yang tidak fit)", new[] { "Kualitas", "Jumlah" }, vm.Kualitas, x => x.Nama, x => x.Jumlah);
            r = ReportExcel.WriteTable(ws, r, "Kondisi fokus (yang tidak fit)", new[] { "Kondisi", "Jumlah" }, vm.Fokus, x => x.Nama, x => x.Jumlah);
            ReportExcel.WriteTable(ws, r, "Konsumsi obat (yang tidak fit)", new[] { "Konsumsi obat", "Jumlah" }, vm.Obat, x => x.Nama, x => x.Jumlah);

            ws = wb.Worksheets.Add("Sering Tidak Fit");
            r = ReportExcel.WriteTitle(ws, $"Karyawan dengan {FrequentUnfitMin} hari tidak fit atau lebih", subtitle, generated);
            ws.Column(1).Style.NumberFormat.Format = "@";
            ReportExcel.WriteTable(ws, r, "Sering tidak fit", new[] { "NIP", "Nama", "Department", "Hari terisi", "Hari tidak fit" },
                vm.FrequentUnfit, e => e.NIP, e => e.Nama, e => e.Department, e => e.HariIsi, e => e.HariTidakFit);

            return ExcelFile(wb, $"Laporan_FitToWork_{vm.From:yyyyMMdd}_{vm.To:yyyyMMdd}.xlsx");
        }

        // ============================ Tindak lanjut tidak fit ============================

        [HttpGet("FollowUps")]
        public async Task<IActionResult> FollowUps(DateTime? from, DateTime? to, string? department, string? status)
        {
            return View(await BuildFollowUpsAsync(from, to, department, status));
        }

        [HttpGet("FollowUps/Export")]
        public async Task<IActionResult> ExportFollowUps(DateTime? from, DateTime? to, string? department, string? status)
        {
            var vm = await BuildFollowUpsAsync(from, to, department, status);
            var subtitle = $"Periode {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy}" + (vm.Department is null ? "" : $" · Department: {vm.Department}");

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Tindak Lanjut Tidak Fit");
            var r = ReportExcel.WriteTitle(ws, "Tindak lanjut karyawan tidak fit", subtitle, $"Dibuat {vm.GeneratedAt:dd/MM/yyyy HH:mm}");
            ws.Column(1).Style.NumberFormat.Format = "@";

            var headers = new List<string> { "NIP", "Nama", "Department", "Section", "No. HP", "Waktu mengisi", "Jadwal", "Area kerja", "Gejala", "Jam tidur", "Kualitas tidur", "Fokus", "Konsumsi obat",
                "Tidak fit 7 hari terakhir (hari)", "Status tindak lanjut", "Prioritas", "Isi ulang FIT" };
            var cols = new List<Func<FtwFollowUpRow, object?>>
            {
                e => e.NIP, e => e.Nama, e => e.Department, e => e.Section, e => e.NomorHp, e => _clock.ToLocal(e.WaktuUtc), e => e.JadwalKerja, e => e.AreaKerja,
                e => e.GejalaKesehatan, e => e.JamTidur, e => e.KualitasTidur, e => e.KondisiFokus, e => e.KonsumsiObat,
                e => e.TidakFit7Hari, e => e.Status, e => e.Prioritas ? "Ya" : "", e => e.IsiUlangFitUtc is DateTime t ? (object)_clock.ToLocal(t) : null
            };
            if (vm.CanSeeClinic)
            {
                headers.AddRange(new[] { "Kunjungan klinik", "Rekomendasi klinik", "Hari istirahat" });
                cols.Add(e => e.KlinikUtc is DateTime t ? (object)_clock.ToLocal(t) : null);
                cols.Add(e => e.KlinikRekomendasi is null ? null : RecommendationTypes.Label(e.KlinikRekomendasi));
                cols.Add(e => e.KlinikHariIstirahat);
            }
            ReportExcel.WriteTable(ws, r, "Kejadian tidak fit", headers.ToArray(), vm.Rows, cols.ToArray());

            return ExcelFile(wb, $"TindakLanjut_TidakFit_{vm.From:yyyyMMdd}_{vm.To:yyyyMMdd}.xlsx");
        }

        // ============================ Membangun data ============================

        private IActionResult ExcelFile(XLWorkbook wb, string name)
        {
            foreach (var ws in wb.Worksheets)
                ws.Columns().AdjustToContents(1, 200);
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), ReportExcel.ContentType, name);
        }

        /// <summary>Menormalkan rentang tanggal (urutan, batas maksimal) dan mengubahnya ke filter UTC.</summary>
        private (DateTime FromD, DateTime ToD, int Days, bool Clamped, FtwReportFilter Filter) Range(DateTime? from, DateTime? to, string? department, DateTime defaultFrom)
        {
            var toD = (to ?? _clock.Today).Date;
            var fromD = (from ?? defaultFrom).Date;
            if (fromD > toD) (fromD, toD) = (toD, fromD);
            var clamped = false;
            if ((toD - fromD).Days + 1 > MaxRangeDays)
            {
                fromD = toD.AddDays(-(MaxRangeDays - 1));
                clamped = true;
            }
            var dept = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
            var filter = new FtwReportFilter(_clock.ToUtc(fromD), _clock.ToUtc(toD.AddDays(1)), dept, _clock.UtcOffsetMinutes);
            return (fromD, toD, (toD - fromD).Days + 1, clamped, filter);
        }

        private async Task<List<string>> DepartmentNamesAsync(string? selected)
        {
            var list = (await _orgRepository.GetDepartmentsAsync(onlyActive: true)).Select(d => d.Name).ToList();
            if (!string.IsNullOrWhiteSpace(selected) && !list.Contains(selected, StringComparer.OrdinalIgnoreCase))
                list.Add(selected);
            return list;
        }

        private async Task<FtwReportViewModel> BuildReportAsync(DateTime? from, DateTime? to, string? department)
        {
            var today = _clock.Today;
            var rg = Range(from, to, department, new DateTime(today.Year, today.Month, 1));
            var f = rg.Filter;

            var employees = await _repo.GetEmployeeComplianceAsync(f);
            var daily = await _repo.GetDailyAsync(f);
            var opsDays = await _repo.GetOperationalDaysAsync(f);
            var breakdown = await _repo.GetUnfitBreakdownAsync(f);

            var vm = new FtwReportViewModel
            {
                From = rg.FromD,
                To = rg.ToD,
                Days = rg.Days,
                Department = f.Department,
                Departments = await DepartmentNamesAsync(f.Department),
                GeneratedAt = _clock.LocalNow,
                RangeWasClamped = rg.Clamped,
                RangeDayLimit = MaxRangeDays,
                KaryawanAktif = employees.Count,
                HariOperasional = opsDays,
                Daily = daily,
                TotalPengisian = daily.Sum(d => d.Isi),
                TotalFit = daily.Sum(d => d.Fit),
                TotalTidakFit = daily.Sum(d => d.TidakFit),
                Employees = employees
            };

            var counted = employees.Where(e => e.HariWajib > 0).ToList();
            var wajib = counted.Sum(e => e.HariWajib);
            vm.KepatuhanPersen = wajib > 0 ? Math.Round(counted.Sum(e => Math.Min(e.HariIsi, e.HariWajib)) * 100m / wajib, 1) : null;
            vm.JumlahTidakPernah = counted.Count(e => e.HariIsi == 0);
            vm.JumlahTertib = counted.Count(e => e.HariIsi > 0 && e.Persen >= TertibMin);
            vm.JumlahCukup = counted.Count(e => e.HariIsi > 0 && e.Persen >= CukupMin && e.Persen < TertibMin);
            vm.JumlahKurang = counted.Count(e => e.HariIsi > 0 && e.Persen < CukupMin);

            // Kepatuhan menurut hari dalam seminggu (Senin..Minggu), dari data harian
            vm.Weekday = Enumerable.Range(0, 7).Select(i =>
            {
                var rows = daily.Where(d => ((int)d.Tanggal.DayOfWeek + 6) % 7 == i).ToList();
                var target = rows.Sum(d => d.Target);
                decimal? p = target > 0 ? Math.Round(Math.Min(rows.Sum(d => d.Isi), target) * 100m / target, 1) : null;
                return (HariId[i], p);
            }).ToList();

            vm.ByDepartment = Group(counted, e => string.IsNullOrWhiteSpace(e.Department) ? "(tanpa department)" : e.Department);
            vm.BySection = Group(counted, e =>
            {
                var sec = string.IsNullOrWhiteSpace(e.Section) ? "(tanpa section)" : e.Section;
                return string.IsNullOrWhiteSpace(e.Department) ? sec : $"{sec} ({e.Department})";
            });

            vm.LowCompliance = counted
                .Where(e => e.HariIsi == 0 || e.Persen < CukupMin)
                .OrderBy(e => e.Persen ?? 0m).ThenByDescending(e => e.BelumIsiBerturut).ThenBy(e => e.Nama)
                .Take(LowComplianceShown).ToList();
            vm.FrequentUnfit = employees
                .Where(e => e.HariTidakFit >= FrequentUnfitMin)
                .OrderByDescending(e => e.HariTidakFit).ThenBy(e => e.Nama)
                .Take(FrequentUnfitShown).ToList();

            vm.ByJadwal = await _repo.GetByJadwalAsync(f);
            var jamTidur = await _repo.GetByJamTidurAsync(f);
            var order = (await _formOptions.GetActiveValuesAsync(FormOptionGroups.JamTidur)).ToList();
            vm.ByJamTidur = jamTidur
                .OrderBy(j => { var i = order.FindIndex(o => string.Equals(o, j.Nama, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                .ThenBy(j => j.Nama).ToList();

            vm.Gejala = SplitGejala(breakdown.Where(b => b.Grup == "Gejala"));
            vm.Kualitas = Merge(breakdown, "Kualitas");
            vm.Fokus = Merge(breakdown, "Fokus");
            vm.Obat = Merge(breakdown, "Obat");
            return vm;
        }

        /// <summary>Mengelompokkan karyawan (untuk department / section), diurutkan dari kepatuhan terendah.</summary>
        private static List<FtwGroupRow> Group(IEnumerable<FtwEmployeeRow> counted, Func<FtwEmployeeRow, string> key)
        {
            return counted
                .GroupBy(key)
                .Select(g => new FtwGroupRow
                {
                    Nama = g.Key,
                    Karyawan = g.Count(),
                    HariWajib = g.Sum(e => e.HariWajib),
                    HariIsi = g.Sum(e => Math.Min(e.HariIsi, e.HariWajib)),
                    HariTidakFit = g.Sum(e => e.HariTidakFit),
                    TidakPernah = g.Count(e => e.HariIsi == 0),
                    Kurang = g.Count(e => e.HariIsi == 0 || e.Persen < CukupMin)
                })
                .OrderBy(g => g.Persen ?? 101m).ThenBy(g => g.Nama)
                .ToList();
        }

        private static List<FtwNameCountRow> Merge(IEnumerable<FtwNameCountRow> rows, string grup) =>
            rows.Where(r => r.Grup == grup && !string.IsNullOrWhiteSpace(r.Nama) && r.Nama != "-")
                .GroupBy(r => r.Nama.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new FtwNameCountRow { Grup = grup, Nama = g.Key, Jumlah = g.Sum(x => x.Jumlah) })
                .OrderByDescending(x => x.Jumlah).ThenBy(x => x.Nama).ToList();

        /// <summary>Gejala disimpan gabungan dipisah koma ("Pusing, Mual"); dipecah supaya tiap gejala terhitung sendiri.</summary>
        private static List<FtwNameCountRow> SplitGejala(IEnumerable<FtwNameCountRow> rows)
        {
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                foreach (var part in r.Nama.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (part != "-")
                        totals[part] = totals.GetValueOrDefault(part) + r.Jumlah;
            return totals.Select(kv => new FtwNameCountRow { Grup = "Gejala", Nama = kv.Key, Jumlah = kv.Value })
                .OrderByDescending(x => x.Jumlah).ThenBy(x => x.Nama).ToList();
        }

        private async Task<FtwFollowUpViewModel> BuildFollowUpsAsync(DateTime? from, DateTime? to, string? department, string? status)
        {
            var access = await _permissions.GetAsync(User);
            var canClinic = access.Has(Permissions.MedicalVisit);
            var rg = Range(from, to, department, _clock.Today.AddDays(-6));

            var rows = await _repo.GetUnfitEventsAsync(rg.Filter, canClinic, FollowUpMax + 1);
            var truncated = rows.Count > FollowUpMax;
            if (truncated) rows = rows.Take(FollowUpMax).ToList();

            var nowUtc = DateTime.UtcNow;
            foreach (var r in rows)
            {
                if (r.KlinikUtc is not null)
                {
                    r.StatusKode = "klinik";
                    r.Status = "Sudah ditangani klinik";
                }
                else if (r.IsiUlangFitUtc is not null)
                {
                    r.StatusKode = "isiulang";
                    r.Status = "Mengisi ulang: FIT";
                }
                else
                {
                    r.StatusKode = "belum";
                    r.Status = "Belum ada tindak lanjut";
                    r.Prioritas = r.TidakFit7Hari >= 2;
                    r.JamTerbuka = (int)Math.Max(0, Math.Floor((nowUtc - DateTime.SpecifyKind(r.WaktuUtc, DateTimeKind.Utc)).TotalHours));
                }
            }

            var vm = new FtwFollowUpViewModel
            {
                From = rg.FromD,
                To = rg.ToD,
                Department = rg.Filter.Department,
                Departments = await DepartmentNamesAsync(rg.Filter.Department),
                GeneratedAt = _clock.LocalNow,
                CanSeeClinic = canClinic,
                Truncated = truncated,
                Kejadian = rows.Count,
                KaryawanUnik = rows.Select(r => r.EmployeeId).Distinct().Count(),
                Belum = rows.Count(r => r.StatusKode == "belum"),
                BelumPrioritas = rows.Count(r => r.StatusKode == "belum" && r.Prioritas),
                Klinik = rows.Count(r => r.StatusKode == "klinik"),
                IsiUlang = rows.Count(r => r.StatusKode == "isiulang")
            };

            var filter = (status ?? "all").Trim().ToLowerInvariant();
            vm.Status = filter is "belum" or "klinik" or "isiulang" ? filter : "all";

            static int Rank(FtwFollowUpRow r) => r.StatusKode == "belum" ? (r.Prioritas ? 0 : 1) : r.StatusKode == "isiulang" ? 2 : 3;
            vm.Rows = rows
                .Where(r => vm.Status == "all" || r.StatusKode == vm.Status)
                .OrderBy(Rank).ThenByDescending(r => r.WaktuUtc)
                .ToList();
            return vm;
        }
    }
}
