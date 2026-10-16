using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/History")]
    public class HistoryController : Controller
    {
        private const int DefaultRangeDays = 7;

        private readonly IFitToWorkRepository _fitToWorkRepository;
        private readonly IFormOptionRepository _formOptionRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IAppClock _clock;

        public HistoryController(
            IFitToWorkRepository fitToWorkRepository,
            IFormOptionRepository formOptionRepository,
            IOrganizationRepository orgRepository,
            IAppClock clock)
        {
            _fitToWorkRepository = fitToWorkRepository;
            _formOptionRepository = formOptionRepository;
            _orgRepository = orgRepository;
            _clock = clock;
        }

        // Halaman riwayat. Tabel diisi per halaman lewat endpoint Data.
        // Default rentang 7 hari terakhir supaya query tetap ringan walau data sudah bertahun-tahun.
        [HttpGet("")]
        [HasPermission(Permissions.FtwHistory)]
        public async Task<IActionResult> Index()
        {
            var today = _clock.Today;
            ViewBag.DefaultFrom = today.AddDays(-(DefaultRangeDays - 1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = today.ToString("yyyy-MM-dd");
            ViewBag.Sections = (await _orgRepository.GetSectionsAsync()).Select(s => s.Name).Distinct().OrderBy(n => n).ToList();
            ViewBag.JadwalKerjaOptions = (await _formOptionRepository.GetActiveValuesAsync(FormOptionGroups.JadwalKerja)).ToList();
            return View();
        }

        // GET Admin/History/Data?draw=..&start=..&length=..&search[value]=..&order[0][column]=..&order[0][dir]=..
        //     &from=2026-09-17&to=2026-09-23&section=..&jadwal=..&kesiapan=fit|unfit
        [HttpGet("Data")]
        [HasPermission(Permissions.FtwHistory)]
        public async Task<IActionResult> Data(int draw, int start, int length,
            DateTime? from, DateTime? to, string? section, string? jadwal, string? kesiapan)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);

            var query = BuildQuery(from, to, section, jadwal, kesiapan,
                Request.Query["search[value]"].ToString(), sortColumn, Request.Query["order[0][dir]"].ToString());
            query.Skip = start;
            query.Take = length;

            var page = await _fitToWorkRepository.GetPageAsync(query);

            return Json(new
            {
                draw,
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                tidakFit = page.FilteredTidakFit,
                data = page.Rows.Select(s => new
                {
                    id = s.Id,
                    waktu = _clock.ToLocal(s.CreatedAt).ToString("dd/MM/yyyy HH:mm"),
                    nip = s.NIP,
                    nama = s.NamaSnapshot,
                    section = s.SectionSnapshot,
                    jadwalKerja = s.JadwalKerja,
                    isFit = s.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase)
                })
            });
        }

        // GET Admin/History/DetailJson/5 -> isi modal detail
        // Detail juga dibuka dari Dashboard, jadi cukup punya salah satu hak akses
        [HttpGet("DetailJson/{id:int}")]
        [HasPermission(Permissions.FtwHistory, Permissions.FtwDashboard)]
        public async Task<IActionResult> DetailJson(int id)
        {
            var s = await _fitToWorkRepository.GetByIdAsync(id);
            if (s is null)
                return NotFound(new { message = "Data pengisian tidak ditemukan." });

            return Json(new
            {
                id = s.Id,
                waktu = _clock.ToLocal(s.CreatedAt).ToString("dd/MM/yyyy HH:mm") + " " + ZonaWaktu(),
                nip = s.NIP,
                nama = s.NamaSnapshot,
                section = s.SectionSnapshot,
                nomorHp = s.NomorHp,
                jadwalKerja = s.JadwalKerja,
                areaKerja = string.IsNullOrEmpty(s.AreaKerjaLainnya) ? s.AreaKerja : $"{s.AreaKerja} ({s.AreaKerjaLainnya})",
                jamTidur = s.JamTidur,
                kualitasTidur = s.KualitasTidur,
                gejalaKesehatan = s.GejalaKesehatan,
                kondisiFokus = s.KondisiFokus,
                konsumsiObat = s.KonsumsiObat,
                kesiapanKerja = s.KesiapanKerja,
                lokasi = LocationDisplay.Text(s),
                mapsUrl = LocationDisplay.MapsUrl(s),
                isFit = s.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase)
            });
        }

        // Halaman detail penuh tetap ada (dipakai tombol Detail di dashboard & bisa dibuka lewat link langsung)
        [HttpGet("Detail/{id:int}")]
        [HasPermission(Permissions.FtwHistory, Permissions.FtwDashboard)]
        public async Task<IActionResult> Detail(int id)
        {
            var submission = await _fitToWorkRepository.GetByIdAsync(id);
            if (submission is null)
                return NotFound();

            return View(submission);
        }

        // ---------------- Export Excel ----------------

        // Batas baris per file. ~20 ribu baris kira-kira 1-2 bulan data; lebih dari itu
        // file jadi berat dibuka & server (limit memori container 512 MB) ikut terbebani.
        public const int MaxExportRows = 20000;

        // GET Admin/History/Export?from=..&to=..&section=..&jadwal=..&kesiapan=..&search=..&sort=0&dir=desc
        // Filter & urutan sama persis dengan tabel yang sedang tampil di layar.
        [HttpGet("Export")]
        [HasPermission(Permissions.FtwHistory)]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? section, string? jadwal,
            string? kesiapan, string? search, int sort = 0, string? dir = "desc")
        {
            var query = BuildQuery(from, to, section, jadwal, kesiapan, search, sort, dir);
            var rows = await _fitToWorkRepository.GetForExportAsync(query, MaxExportRows + 1);

            var terpotong = rows.Count > MaxExportRows;
            if (terpotong)
                rows.RemoveAt(rows.Count - 1);

            var periode = (from, to) switch
            {
                (null, null) => "Semua tanggal",
                (DateTime f, null) => $"Mulai {f:dd/MM/yyyy}",
                (null, DateTime t) => $"Sampai {t:dd/MM/yyyy}",
                (DateTime f, DateTime t) => f.Date == t.Date ? $"{f:dd/MM/yyyy}" : $"{f:dd/MM/yyyy} - {t:dd/MM/yyyy}"
            };

            var filterParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(section)) filterParts.Add($"Section: {section}");
            if (!string.IsNullOrWhiteSpace(jadwal)) filterParts.Add($"Jadwal: {jadwal}");
            if (kesiapan == "fit") filterParts.Add("Kesiapan: FIT");
            if (kesiapan == "unfit") filterParts.Add("Kesiapan: TIDAK FIT");
            if (!string.IsNullOrWhiteSpace(search)) filterParts.Add($"Cari: \"{search.Trim()}\"");
            var filterText = filterParts.Count == 0 ? "Tanpa filter" : string.Join(" · ", filterParts);

            var content = BuildHistoryWorkbook(rows, periode, filterText, terpotong);

            var fileTag = (from, to) switch
            {
                (DateTime f, DateTime t) => f.Date == t.Date ? $"{f:yyyyMMdd}" : $"{f:yyyyMMdd}-{t:yyyyMMdd}",
                _ => $"s.d.{_clock.Today:yyyyMMdd}"
            };

            return File(content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Riwayat_FitToWork_{fileTag}.xlsx");
        }

        private byte[] BuildHistoryWorkbook(List<FitToWorkSubmission> rows, string periode, string filterText, bool terpotong)
        {
            var zona = ZonaWaktu();
            var headerFill = XLColor.FromHtml("#1F3864");
            var unfitFill = XLColor.FromHtml("#F8D7DA");
            var unfitFont = XLColor.FromHtml("#842029");
            var fitFont = XLColor.FromHtml("#0F5132");

            using var wb = new XLWorkbook();

            // ================= Sheet 1: Riwayat (detail per pengisian) =================
            var ws = wb.Worksheets.Add("Riwayat");

            string[] headers =
            {
                "No", "Tanggal", $"Jam ({zona})", "NIP", "Nama", "Section", "No. HP", "Jadwal Kerja", "Area Kerja",
                "Jam Tidur", "Kualitas Tidur", "Gejala Kesehatan", "Kondisi Fokus & Pikiran",
                "Konsumsi Obat 12 Jam", "Status", "Jawaban Kesiapan Kerja", "Lokasi Pengisian", "Jarak ke Site (m)"
            };
            double[] widths = { 6, 12, 10, 12, 28, 26, 15, 14, 20, 12, 22, 32, 24, 12, 11, 40, 22, 12 };
            const int headerRow = 4;
            const int statusCol = 15;

            ws.Cell(1, 1).Value = "Riwayat Pengisian Fit To Work";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {periode}   |   {filterText}   |   Diunduh: {_clock.LocalNow:dd/MM/yyyy HH:mm} {zona}";
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
            if (terpotong)
            {
                ws.Cell(3, 1).Value = $"⚠ Data dipotong: hanya {MaxExportRows:N0} baris pertama yang disertakan. Persempit rentang tanggal untuk data lengkap.";
                ws.Cell(3, 1).Style.Font.FontColor = unfitFont;
                ws.Cell(3, 1).Style.Font.Bold = true;
            }

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(headerRow, c + 1).Value = headers[c];
                ws.Column(c + 1).Width = widths[c];
            }

            var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = headerFill;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;
            ws.Row(headerRow).Height = 30;

            var r = headerRow + 1;
            var no = 1;
            foreach (var s in rows)
            {
                var local = _clock.ToLocal(s.CreatedAt);
                var isFit = s.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase);
                var area = string.IsNullOrEmpty(s.AreaKerjaLainnya) ? s.AreaKerja : $"{s.AreaKerja} ({s.AreaKerjaLainnya})";

                ws.Cell(r, 1).Value = no++;
                ws.Cell(r, 2).Value = local.Date;                       // tanggal asli (bisa di-filter/sortir di Excel)
                ws.Cell(r, 3).Value = local.ToString("HH:mm");
                ws.Cell(r, 4).Value = s.NIP ?? string.Empty;            // teks, supaya NIP tidak berubah jadi angka
                ws.Cell(r, 5).Value = s.NamaSnapshot;
                ws.Cell(r, 6).Value = s.SectionSnapshot;
                ws.Cell(r, 7).Value = s.NomorHp ?? string.Empty;        // teks, supaya angka 0 di depan tidak hilang
                ws.Cell(r, 8).Value = s.JadwalKerja;
                ws.Cell(r, 9).Value = area;
                ws.Cell(r, 10).Value = s.JamTidur;
                ws.Cell(r, 11).Value = s.KualitasTidur;
                ws.Cell(r, 12).Value = s.GejalaKesehatan;
                ws.Cell(r, 13).Value = s.KondisiFokus;
                ws.Cell(r, 14).Value = s.KonsumsiObat;
                ws.Cell(r, statusCol).Value = isFit ? "FIT" : "TIDAK FIT";
                ws.Cell(r, 16).Value = s.KesiapanKerja;
                ws.Cell(r, 17).Value = s.LokasiVia switch
                {
                    "WIFI" => $"{s.LokasiNama} (Wi-Fi)",
                    "GPS" => $"{s.LokasiNama} (GPS)",
                    _ => string.Empty
                };
                if (s.JarakMeter is int jarak)
                    ws.Cell(r, 18).Value = jarak;

                var status = ws.Cell(r, statusCol);
                status.Style.Font.Bold = true;
                if (isFit)
                {
                    status.Style.Font.FontColor = fitFont;
                }
                else
                {
                    // Seluruh baris TIDAK FIT diberi latar merah muda supaya cepat terlihat
                    ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = unfitFill;
                    status.Style.Font.FontColor = unfitFont;
                }
                r++;
            }

            var lastRow = Math.Max(r - 1, headerRow);
            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(4).Style.NumberFormat.Format = "@";
            ws.Column(7).Style.NumberFormat.Format = "@";
            ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();   // tombol filter di tiap judul kolom
            ws.SheetView.FreezeRows(headerRow);                                  // judul kolom tetap terlihat saat scroll

            // ================= Sheet 2: Ringkasan =================
            var sum = wb.Worksheets.Add("Ringkasan");
            sum.Column(1).Width = 30;
            sum.Column(2).Width = 12;
            sum.Column(3).Width = 12;
            sum.Column(4).Width = 12;
            sum.Column(5).Width = 12;

            sum.Cell(1, 1).Value = "Ringkasan Fit To Work";
            sum.Cell(1, 1).Style.Font.Bold = true;
            sum.Cell(1, 1).Style.Font.FontSize = 14;
            sum.Cell(2, 1).Value = $"Periode: {periode}";
            sum.Cell(3, 1).Value = filterText;
            sum.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
            sum.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");

            var totalFit = rows.Count(x => x.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase));
            var totalUnfit = rows.Count - totalFit;

            sum.Cell(5, 1).Value = "Total pengisian";
            sum.Cell(5, 2).Value = rows.Count;
            sum.Cell(6, 1).Value = "FIT";
            sum.Cell(6, 2).Value = totalFit;
            sum.Cell(7, 1).Value = "TIDAK FIT";
            sum.Cell(7, 2).Value = totalUnfit;
            sum.Cell(8, 1).Value = "Karyawan berbeda";
            sum.Cell(8, 2).Value = rows.Select(x => x.EmployeeId).Distinct().Count();
            sum.Range(5, 1, 8, 1).Style.Font.Bold = true;
            sum.Cell(7, 2).Style.Font.FontColor = unfitFont;

            // Tabel kecil: rekap per Section dan per Jadwal Kerja
            var nextRow = WriteBreakdown(sum, 10, "Per Section", rows.GroupBy(x => x.SectionSnapshot), headerFill, unfitFont);
            WriteBreakdown(sum, nextRow + 2, "Per Jadwal Kerja", rows.GroupBy(x => x.JadwalKerja), headerFill, unfitFont);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        // Menulis tabel rekap (Nama | Total | FIT | TIDAK FIT | % TIDAK FIT) mulai dari baris startRow.
        // Mengembalikan nomor baris terakhir yang terisi.
        private static int WriteBreakdown(IXLWorksheet ws, int startRow, string title,
            IEnumerable<IGrouping<string, FitToWorkSubmission>> groups, XLColor headerFill, XLColor unfitFont)
        {
            string[] headers = { title, "Total", "FIT", "TIDAK FIT", "% TIDAK FIT" };
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(startRow, c + 1).Value = headers[c];

            var head = ws.Range(startRow, 1, startRow, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = headerFill;

            var r = startRow + 1;
            foreach (var g in groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                var total = g.Count();
                var unfit = g.Count(x => !x.KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase));

                ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(g.Key) ? "(kosong)" : g.Key;
                ws.Cell(r, 2).Value = total;
                ws.Cell(r, 3).Value = total - unfit;
                ws.Cell(r, 4).Value = unfit;
                ws.Cell(r, 5).Value = total == 0 ? 0 : (double)unfit / total;
                ws.Cell(r, 5).Style.NumberFormat.Format = "0.0%";
                if (unfit > 0)
                    ws.Cell(r, 4).Style.Font.FontColor = unfitFont;
                r++;
            }

            if (r == startRow + 1)
            {
                ws.Cell(r, 1).Value = "(tidak ada data)";
                r++;
            }

            ws.Range(startRow, 1, r - 1, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            return r - 1;
        }

        // Dipakai bersama oleh Data (tabel) dan Export (Excel) supaya filternya selalu sama.
        // Tanggal dari form adalah tanggal lokal site (WITA); kolom CreatedAt disimpan UTC.
        private HistoryPageQuery BuildQuery(DateTime? from, DateTime? to, string? section, string? jadwal,
            string? kesiapan, string? search, int sortColumn, string? sortDir) => new()
        {
            Search = search,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Section = section,
            JadwalKerja = jadwal,
            IsFit = kesiapan switch { "fit" => true, "unfit" => false, _ => null },
            SortColumn = sortColumn,
            SortDesc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
        };

        private string ZonaWaktu() => _clock.UtcOffsetMinutes switch
        {
            420 => "WIB",
            480 => "WITA",
            540 => "WIT",
            _ => _clock.TimeZoneId
        };
    }
}
