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
        private readonly IFtwFollowUpRepository _followUpRepo;
        private readonly IFitToWorkRepository _ftwRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IFormOptionRepository _formOptions;
        private readonly IPermissionService _permissions;
        private readonly IAppClock _clock;

        public FtwReportsController(IFtwReportRepository repo, IOrganizationRepository orgRepository,
            IFormOptionRepository formOptions, IPermissionService permissions, IAppClock clock,
            IFtwFollowUpRepository followUpRepo, IFitToWorkRepository ftwRepository)
        {
            _followUpRepo = followUpRepo;
            _ftwRepository = ftwRepository;
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
            headers.AddRange(new[] { "Follow up online", "Kanal", "Waktu follow up", "Oleh", "Catatan follow up", "Jumlah follow up", "Kasus selesai", "Follow up berikutnya" });
            cols.Add(e => e.OnlineId.HasValue ? FtwFollowUpHasil.Label(e.OnlineHasil) : null);
            cols.Add(e => e.OnlineKanal is null ? null : FtwFollowUpKanal.Label(e.OnlineKanal));
            cols.Add(e => e.OnlineAtUtc is DateTime t ? (object)_clock.ToLocal(t) : null);
            cols.Add(e => e.OnlineBy);
            cols.Add(e => e.OnlineCatatan);
            cols.Add(e => e.OnlineCount);
            cols.Add(e => e.OnlineId.HasValue ? (e.OnlineClosed == true ? "Ya" : "Belum") : null);
            cols.Add(e => e.OnlineNext is DateTime nx && e.OnlineClosed != true ? (object)nx.Date : null);
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

        // ============================ Follow up online ============================

        // Mencatat hasil menghubungi karyawan (WhatsApp / telepon). TIDAK membuat kunjungan klinik dan tidak masuk laporan klinik.
        [HttpPost("FollowUps/Online")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FollowUpOnline(int ftwId, string? kanal, string? hasil, string? catatan, bool? closed, DateTime? next,
            string? from, string? to, string? department, string? status)
        {
            var back = RedirectToAction(nameof(FollowUps), new { from, to, department, status });
            if (!await _followUpRepo.IsAvailableAsync())
            {
                TempData["Error"] = "Fitur follow up online belum aktif di database (jalankan migration_ftw_followup_online.sql).";
                return back;
            }
            if (!FtwFollowUpKanal.All.Contains(kanal ?? "") || !FtwFollowUpHasil.All.Contains(hasil ?? ""))
            {
                TempData["Error"] = "Pilih kanal dan hasil follow up.";
                return back;
            }
            var ftw = await _ftwRepository.GetByIdAsync(ftwId);
            if (ftw is null || ftw.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Kejadian tidak fit tidak ditemukan.";
                return back;
            }

            var note = string.IsNullOrWhiteSpace(catatan) ? null : catatan.Trim();
            if (note is { Length: > 1000 }) note = note[..1000];

            // Kasus ditutup hanya kalau masalahnya selesai. Yang belum selesai tetap terbuka dan WAJIB punya jadwal follow up berikutnya
            // (default besok; maksimal 30 hari ke depan), supaya tidak terlupakan dan bisa ditandai terlambat.
            var isClosed = closed ?? hasil == FtwFollowUpHasil.Membaik;
            DateTime? nextDate = null;
            if (!isClosed)
            {
                var today = _clock.Today;
                nextDate = next?.Date ?? today.AddDays(hasil == FtwFollowUpHasil.PerluKlinik ? 0 : 1);
                if (nextDate < today) nextDate = today;
                if (nextDate > today.AddDays(30)) nextDate = today.AddDays(30);
            }
            await _followUpRepo.AddAsync(new FtwFollowUpLog
            {
                FtwSubmissionId = ftw.Id,
                Kanal = kanal!,
                Hasil = hasil!,
                Catatan = note,
                FollowUpAt = DateTime.UtcNow,
                IsClosed = isClosed,
                NextFollowUpDate = nextDate,
                ByAdminId = User.AdminId(),
                ByName = User.AdminName()
            });
            TempData["Success"] = isClosed
                ? $"Follow up online {ftw.NamaSnapshot} dicatat ({FtwFollowUpHasil.Label(hasil)}). Kasus ditutup. Tidak dihitung sebagai kunjungan klinik."
                : $"Follow up online {ftw.NamaSnapshot} dicatat ({FtwFollowUpHasil.Label(hasil)}). Kasus BELUM selesai; follow up berikutnya {nextDate:dd/MM/yyyy}. Tidak dihitung sebagai kunjungan klinik.";
            return back;
        }

        // ============================ Hasil screening FTW & pemeriksaan ============================

        private const int ScreeningPageMax = 1000;
        private const int ScreeningExcelMax = 5000;

        [HttpGet("Screening")]
        public async Task<IActionResult> Screening(DateTime? from, DateTime? to, string? department, string? scope)
        {
            return View(await BuildScreeningAsync(from, to, department, scope, ScreeningPageMax));
        }

        [HttpGet("Screening/Export")]
        public async Task<IActionResult> ExportScreening(DateTime? from, DateTime? to, string? department, string? scope)
        {
            var vm = await BuildScreeningAsync(from, to, department, scope, ScreeningExcelMax);
            var clinic = vm.CanSeeClinic;
            var lastCol = clinic ? 15 : 11;

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Screening FTW");

            ws.Cell(1, 1).Value = "LAPORAN HASIL SCREENING FIT TO WORK" + (clinic ? " DAN PEMERIKSAAN KLINIK" : "");
            ws.Range(1, 1, 1, lastCol).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(2, 1).Value = $"Periode {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy}" + (vm.Department is null ? "" : $" · Department: {vm.Department}")
                                  + (vm.Scope == "diperiksa" ? " · Hanya yang sudah diperiksa klinik" : "");
            ws.Range(2, 1, 2, lastCol).Merge();
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Header dua tingkat: kolom identitas & hasil digabung vertikal, 5 kolom screening berada di bawah "HASIL SCREENING FTW"
            const int h1 = 4, h2 = 5, first = 6;
            string[] single = { "NO", "TANGGAL", "NAMA", "SECTION", "JADWAL KERJA", "AREA KERJA" };
            for (var c = 1; c <= single.Length; c++)
            {
                ws.Cell(h1, c).Value = single[c - 1];
                ws.Range(h1, c, h2, c).Merge();
            }
            ws.Cell(h1, 7).Value = "HASIL SCREENING FTW";
            ws.Range(h1, 7, h1, 11).Merge();
            string[] sub = { "JUMLAH JAM TIDUR", "KUALITAS TIDUR", "GEJALA KESEHATAN", "KONDISI FOKUS DAN PIKIRAN", "KONSUMSI OBAT" };
            for (var i = 0; i < sub.Length; i++) ws.Cell(h2, 7 + i).Value = sub[i];
            if (clinic)
            {
                string[] result = { "HASIL PEMERIKSAAN", "TINDAKAN", "REKOMENDASI", "STATUS KASUS" };
                for (var i = 0; i < result.Length; i++)
                {
                    ws.Cell(h1, 12 + i).Value = result[i];
                    ws.Range(h1, 12 + i, h2, 12 + i).Merge();
                }
            }
            var head = ws.Range(h1, 1, h2, lastCol);
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#9BC2E6");
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            head.Style.Alignment.WrapText = true;

            var r = first;
            var no = 1;
            foreach (var x in vm.Rows)
            {
                ReportExcel.SetCell(ws.Cell(r, 1), no++);
                ReportExcel.SetCell(ws.Cell(r, 2), x.Tanggal);
                ReportExcel.SetCell(ws.Cell(r, 3), x.Nama.ToUpperInvariant());
                ReportExcel.SetCell(ws.Cell(r, 4), x.Section.ToUpperInvariant());
                ReportExcel.SetCell(ws.Cell(r, 5), x.JadwalKerja);
                ReportExcel.SetCell(ws.Cell(r, 6), x.AreaKerja);
                ReportExcel.SetCell(ws.Cell(r, 7), x.JamTidur);
                ReportExcel.SetCell(ws.Cell(r, 8), x.KualitasTidur);
                ReportExcel.SetCell(ws.Cell(r, 9), x.GejalaKesehatan);
                ReportExcel.SetCell(ws.Cell(r, 10), x.KondisiFokus);
                ReportExcel.SetCell(ws.Cell(r, 11), x.KonsumsiObat);
                if (clinic)
                {
                    ReportExcel.SetCell(ws.Cell(r, 12), x.HasilPemeriksaan);
                    ReportExcel.SetCell(ws.Cell(r, 13), x.Tindakan);
                    ReportExcel.SetCell(ws.Cell(r, 14), x.RekomendasiText);
                    ReportExcel.SetCell(ws.Cell(r, 15), x.StatusKasus);
                }
                r++;
            }

            var last = Math.Max(r - 1, first);
            var table = ws.Range(h1, 1, last, lastCol);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            if (vm.Rows.Count > 0)
            {
                var body = ws.Range(first, 1, last, lastCol);
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                body.Style.Alignment.WrapText = true;
                body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (clinic) ws.Range(first, 12, last, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            }
            else
            {
                ws.Cell(first, 1).Value = "(tidak ada data)";
                ws.Range(first, 1, first, lastCol).Merge();
                ws.Cell(first, 1).Style.Font.Italic = true;
            }

            double[] widths = { 5, 12, 28, 14, 14, 16, 14, 30, 26, 28, 14, 32, 34, 24, 34 };
            for (var c = 1; c <= lastCol; c++) ws.Column(c).Width = widths[c - 1];

            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.SetRowsToRepeatAtTop(h1, h2);
            ws.SheetView.FreezeRows(h2);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), ReportExcel.ContentType, $"Screening_FTW_Pemeriksaan_{vm.From:yyyyMMdd}_{vm.To:yyyyMMdd}.xlsx");
        }

        private async Task<FtwScreeningViewModel> BuildScreeningAsync(DateTime? from, DateTime? to, string? department, string? scope, int max)
        {
            var access = await _permissions.GetAsync(User);
            var canClinic = access.Has(Permissions.MedicalVisit);
            var rg = Range(from, to, department, _clock.Today.AddDays(-6));

            var rows = await _repo.GetScreeningAsync(rg.Filter, canClinic, max + 1, await _repo.IsOnlineFollowUpAvailableAsync());
            var truncated = rows.Count > max;
            if (truncated) rows = rows.Take(max).ToList();

            // 1 baris per karyawan per hari: pengisian tidak fit terakhir pada hari itu (hasil query terurut terbaru dulu)
            rows = rows.GroupBy(r => (r.EmployeeId, _clock.ToLocal(r.WaktuUtc).Date)).Select(g => g.First()).ToList();

            foreach (var r in rows)
            {
                r.Tanggal = _clock.ToLocal(r.WaktuUtc).Date;
                var hasil = new List<string>();
                if (!string.IsNullOrWhiteSpace(r.Diagnosa)) hasil.Add(r.Diagnosa.Trim());
                if (!string.IsNullOrWhiteSpace(r.Pemeriksaan))
                    hasil.Add(hasil.Count > 0 ? $"({r.Pemeriksaan.Trim()})" : r.Pemeriksaan.Trim());
                r.HasilPemeriksaan = string.Join("\n", hasil);

                var tindakan = new List<string>();
                if (!string.IsNullOrWhiteSpace(r.Obat)) tindakan.Add(r.Obat.Trim());
                if (!string.IsNullOrWhiteSpace(r.CatatanRekomendasi)) tindakan.Add(r.CatatanRekomendasi.Trim());
                r.Tindakan = string.Join("\n", tindakan);

                // Status kasus: Selesai bila sudah diperiksa klinik atau follow up online ditutup; selain itu Belum close
                if (r.Diperiksa) { r.Close = true; r.StatusDetail = "diperiksa klinik"; }
                else if (r.OnlineId.HasValue && r.OnlineClosed == true) { r.Close = true; r.StatusDetail = "follow up online: " + FtwFollowUpHasil.Label(r.OnlineHasil); }
                else if (r.OnlineId.HasValue)
                {
                    var late = r.OnlineNext is DateTime nd && nd.Date < _clock.Today;
                    r.StatusDetail = "online: " + FtwFollowUpHasil.Label(r.OnlineHasil)
                        + (r.OnlineNext is DateTime n2 ? $", follow up {n2:dd/MM/yyyy}{(late ? " (terlambat)" : "")}" : "");
                }
                else r.StatusDetail = "belum ditindaklanjuti";
                r.StatusKasus = (r.Close ? "Selesai" : "Belum close") + " - " + r.StatusDetail;

                // Istilah "LANJUT BEKERJA" mengikuti format laporan yang dipakai (di aplikasi: "Kembali bekerja")
                r.RekomendasiText = r.Rekomendasi is null
                    ? ""
                    : r.Rekomendasi == RecommendationTypes.KembaliBekerja
                        ? "LANJUT BEKERJA"
                        : RecommendationTypes.Label(r.Rekomendasi, r.HariIstirahat, r.IstirahatDari, r.IstirahatSampai).ToUpperInvariant();
            }

            var onlyExamined = canClinic && string.Equals(scope, "diperiksa", StringComparison.OrdinalIgnoreCase);
            var vm = new FtwScreeningViewModel
            {
                From = rg.FromD,
                To = rg.ToD,
                Department = rg.Filter.Department,
                Scope = onlyExamined ? "diperiksa" : "all",
                Departments = await DepartmentNamesAsync(rg.Filter.Department),
                GeneratedAt = _clock.LocalNow,
                CanSeeClinic = canClinic,
                Truncated = truncated,
                Total = rows.Count,
                Diperiksa = rows.Count(r => r.Diperiksa),
                BelumClose = rows.Count(r => !r.Close)
            };
            vm.Rows = rows
                .Where(r => !onlyExamined || r.Diperiksa)
                .OrderBy(r => r.Tanggal).ThenBy(r => r.Nama)
                .ToList();
            return vm;
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
            var canTriage = access.Has(Permissions.MedicalTriage) || canClinic;
            var triageAvailable = await _repo.IsTriageAvailableAsync();
            var onlineAvailable = await _repo.IsOnlineFollowUpAvailableAsync();
            var rg = Range(from, to, department, _clock.Today.AddDays(-6));

            var rows = await _repo.GetUnfitEventsAsync(rg.Filter, canClinic, FollowUpMax + 1, triageAvailable, onlineAvailable);
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
                else if (r.TriageStatus == TriageStatuses.Selesai)
                {
                    // Dokter sudah memfinalkan kunjungan dari pemeriksaan awal (detail klinik hanya tampil untuk yang berizin)
                    r.StatusKode = "klinik";
                    r.Status = "Sudah ditangani klinik";
                }
                else if (r.TriageStatus == TriageStatuses.Draft)
                {
                    r.StatusKode = "triage";
                    r.Status = "Pemeriksaan awal: menunggu dokter";
                }
                else if (r.OnlineId.HasValue && r.OnlineClosed == true)
                {
                    // Dihubungi dan masalah selesai: kasus ditutup. TIDAK dihitung sebagai kunjungan klinik.
                    r.StatusKode = "online";
                    r.Status = "Follow up online: selesai (" + FtwFollowUpHasil.Label(r.OnlineHasil) + ")";
                }
                else if (r.OnlineId.HasValue)
                {
                    // Dihubungi tapi masalah BELUM selesai: kasus tetap terbuka sampai ditutup, ditandai terlambat kalau jadwal terlewat
                    r.StatusKode = "lanjut";
                    r.Status = "Follow up online: belum selesai (" + FtwFollowUpHasil.Label(r.OnlineHasil) + ")";
                    r.LanjutTerlambat = r.OnlineNext is DateTime nd && nd.Date < _clock.Today;
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
                CanTriage = canTriage,
                CanDoctor = canClinic,
                TriageAvailable = triageAvailable,
                OnlineAvailable = onlineAvailable,
                Truncated = truncated,
                Kejadian = rows.Count,
                KaryawanUnik = rows.Select(r => r.EmployeeId).Distinct().Count(),
                Belum = rows.Count(r => r.StatusKode == "belum"),
                BelumPrioritas = rows.Count(r => r.StatusKode == "belum" && r.Prioritas),
                Klinik = rows.Count(r => r.StatusKode == "klinik"),
                MenungguDokter = rows.Count(r => r.StatusKode == "triage"),
                Online = rows.Count(r => r.StatusKode == "online"),
                Lanjut = rows.Count(r => r.StatusKode == "lanjut"),
                LanjutTerlambat = rows.Count(r => r.StatusKode == "lanjut" && r.LanjutTerlambat),
                IsiUlang = rows.Count(r => r.StatusKode == "isiulang")
            };

            var filter = (status ?? "all").Trim().ToLowerInvariant();
            vm.Status = filter is "belum" or "triage" or "lanjut" or "online" or "klinik" or "isiulang" ? filter : "all";

            // Yang butuh tindakan di atas: belum ditangani, lalu follow up online belum selesai (terlambat dulu), lalu draft menunggu dokter
            static int Rank(FtwFollowUpRow r) => r.StatusKode == "belum" ? (r.Prioritas ? 0 : 1)
                : r.StatusKode == "lanjut" ? (r.LanjutTerlambat ? 2 : 3)
                : r.StatusKode == "triage" ? 4 : r.StatusKode == "isiulang" ? 5 : r.StatusKode == "online" ? 6 : 7;
            vm.Rows = rows
                .Where(r => vm.Status == "all" || r.StatusKode == vm.Status)
                .OrderBy(Rank).ThenByDescending(r => r.WaktuUtc)
                .ToList();
            return vm;
        }
    }
}
