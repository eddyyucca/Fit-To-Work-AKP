using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>
    /// Riwayat Aktivitas Kebugaran (gym, lari, hiking, dll) untuk admin. Polanya sama dengan HistoryController.
    /// URL tetap /Admin/Gym supaya bookmark lama tetap jalan.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.GymHistory)]
    [Route("Admin/Gym")]
    public class GymHistoryController : Controller
    {
        private const int DefaultRangeDays = 7;
        public const int MaxExportRows = 20000;

        private readonly IGymRepository _gymRepository;
        private readonly IActivityTypeRepository _typeRepository;
        private readonly IOrganizationRepository _orgRepository;
        private readonly IAppClock _clock;

        public GymHistoryController(IGymRepository gymRepository, IActivityTypeRepository typeRepository,
            IOrganizationRepository orgRepository, IAppClock clock)
        {
            _gymRepository = gymRepository;
            _typeRepository = typeRepository;
            _orgRepository = orgRepository;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var today = _clock.Today;
            ViewBag.DefaultFrom = today.AddDays(-(DefaultRangeDays - 1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = today.ToString("yyyy-MM-dd");
            ViewBag.Departments = (await _orgRepository.GetDepartmentsAsync()).Select(d => d.Name).Distinct().OrderBy(n => n).ToList();
            ViewBag.Positions = (await _orgRepository.GetPositionsAsync()).OrderBy(p => p.SortOrder).Select(p => p.Name).Distinct().ToList();
            ViewBag.Types = await _typeRepository.GetAllAsync();
            return View();
        }

        // GET Admin/Gym/Data?draw=..&start=..&length=..&search[value]=..&order[0][column]=..&order[0][dir]=..
        //     &from=2026-09-17&to=2026-09-23&department=..&position=..&status=ingym|selesai|tidak|lama&type=2
        [HttpGet("Data")]
        public async Task<IActionResult> Data(int draw, int start, int length,
            DateTime? from, DateTime? to, string? department, string? position, string? status, int? type)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);

            var query = BuildQuery(from, to, department, position, status, type,
                Request.Query["search[value]"].ToString(), sortColumn, Request.Query["order[0][dir]"].ToString());
            query.Skip = start;
            query.Take = length;

            var page = await _gymRepository.GetPageAsync(query);
            var nowUtc = DateTime.UtcNow;

            return Json(new
            {
                draw,
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                karyawan = page.FilteredEmployees,
                rataDurasi = page.RataDurasiMenit is decimal r ? GymRules.FormatDuration((int)Math.Round(r)) : null,
                tidakCheckout = page.TidakCheckout,
                totalJarak = page.TotalJarakMeter > 0 ? GymRules.FormatDistance((int)Math.Min(page.TotalJarakMeter, int.MaxValue)) : null,
                sedangDiGym = page.SedangDiGym,
                data = page.Rows.Select(g =>
                {
                    var state = GymRules.GetState(g, nowUtc);
                    var selesai = state == GymRules.SessionState.Finished;
                    return new
                    {
                        id = g.Id,
                        waktu = _clock.ToLocal(g.CreatedAt).ToString("dd/MM/yyyy HH:mm"),
                        checkOut = g.CheckOutAt.HasValue ? _clock.ToLocal(g.CheckOutAt.Value).ToString("HH:mm") : null,
                        durasi = g.DurasiMenit is null ? null : GymRules.FormatDuration(g.DurasiMenit),
                        aktivitas = g.Aktivitas,
                        ikon = g.Ikon,
                        jarak = selesai && g.JarakTempuhMeter is not null ? GymRules.FormatDistance(g.JarakTempuhMeter) : null,
                        pace = selesai ? GymRules.FormatPace(g.DurasiDetik, g.JarakTempuhMeter) : null,
                        gps = g.IsGps,
                        rute = g.PunyaRute,
                        status = GymRules.StatusLabel(state),
                        statusBadge = GymRules.StatusBadge(state),
                        nip = g.NIP,
                        nama = g.NamaSnapshot,
                        department = g.DepartmentSnapshot,
                        jabatan = g.PositionSnapshot,
                        beratBadan = g.BeratBadan,
                        lokasi = LocationDisplay.Text(g),
                        mapsUrl = LocationDisplay.MapsUrl(g)
                    };
                })
            });
        }

        // GET Admin/Gym/Route/5 -> data peta rute (modal)
        [HttpGet("Route/{id:int}")]
        public async Task<IActionResult> Route(int id)
        {
            var g = await _gymRepository.GetByIdAsync(id);
            if (g is null)
                return NotFound();
            return Json(RouteJson(g, _clock, withEmployee: true));
        }

        /// <summary>JSON untuk wwwroot/js/route-map.js (dipakai juga oleh Riwayat Saya karyawan).</summary>
        public static object RouteJson(GymAttendance g, IAppClock clock, bool withEmployee)
        {
            var mulai = clock.ToLocal(g.CreatedAt);
            var ringkasan = new List<string[]>
            {
                new[] { "Durasi", GymRules.FormatDuration(g.DurasiMenit) },
                new[] { "Jarak", GymRules.FormatDistance(g.JarakTempuhMeter) },
                new[] { "Pace", GymRules.FormatPace(g.DurasiDetik, g.JarakTempuhMeter) ?? "-" },
                new[] { "Kecepatan rata-rata", GymRules.FormatSpeed(g.DurasiDetik, g.JarakTempuhMeter) ?? "-" }
            };
            var catatan = new List<string>();
            if (g.GpsTitik is int titik)
                catatan.Add($"{titik:N0} titik GPS dipakai");
            if (g.GpsCelahDetik is int celah && celah >= 60)
                catatan.Add($"GPS sempat berhenti ±{Math.Max(1, (int)Math.Round(celah / 60d))} menit (jarak di jeda dihitung garis lurus)");
            if (string.IsNullOrEmpty(g.RutePolyline))
                catatan.Add("Rute tidak tersedia");

            return new
            {
                judul = $"{g.Ikon} {g.Aktivitas}" + (withEmployee ? $" · {g.NamaSnapshot}" : ""),
                sub = $"{mulai:dd/MM/yyyy HH:mm}" + (g.CheckOutAt is DateTime co ? $" - {clock.ToLocal(co):HH:mm}" : "")
                      + (withEmployee && !string.IsNullOrEmpty(g.NIP) ? $" · NIK {g.NIP}" : ""),
                polyline = g.RutePolyline,
                ringkasan,
                catatan = string.Join(" · ", catatan)
            };
        }

        // GET Admin/Gym/Export?from=..&to=..&department=..&position=..&type=..&search=..&sort=0&dir=desc
        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department, string? position,
            string? status, int? type, string? search, int sort = 0, string? dir = "desc")
        {
            var query = BuildQuery(from, to, department, position, status, type, search, sort, dir);
            var rows = await _gymRepository.GetForExportAsync(query, MaxExportRows + 1);

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
            if (!string.IsNullOrWhiteSpace(department)) filterParts.Add($"Department: {department}");
            if (!string.IsNullOrWhiteSpace(position)) filterParts.Add($"Jabatan: {position}");
            if (type is int typeId && await _typeRepository.GetByIdAsync(typeId) is ActivityType at) filterParts.Add($"Aktivitas: {at.Nama}");
            if (StatusFilterLabel(status) is string sl) filterParts.Add($"Status: {sl}");
            if (!string.IsNullOrWhiteSpace(search)) filterParts.Add($"Cari: \"{search.Trim()}\"");
            var filterText = filterParts.Count == 0 ? "Tanpa filter" : string.Join(" · ", filterParts);

            var content = BuildWorkbook(rows, periode, filterText, terpotong);

            var fileTag = (from, to) switch
            {
                (DateTime f, DateTime t) => f.Date == t.Date ? $"{f:yyyyMMdd}" : $"{f:yyyyMMdd}-{t:yyyyMMdd}",
                _ => $"s.d.{_clock.Today:yyyyMMdd}"
            };

            return File(content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Aktivitas_Kebugaran_{fileTag}.xlsx");
        }

        private byte[] BuildWorkbook(List<GymAttendance> rows, string periode, string filterText, bool terpotong)
        {
            var zona = ZonaWaktu();
            var headerFill = XLColor.FromHtml("#1F3864");
            var warnFont = XLColor.FromHtml("#842029");

            using var wb = new XLWorkbook();

            // ================= Sheet 1: Daftar Hadir =================
            var ws = wb.Worksheets.Add("Riwayat Aktivitas");
            string[] headers =
            {
                "No", "Tanggal", "Aktivitas", $"Mulai ({zona})", $"Selesai ({zona})", "Durasi (menit)", "Jarak (km)", "Pace (menit/km)", "Status",
                "NIK", "Nama Lengkap", "Department", "Section", "Jabatan", "Berat Badan (kg)",
                "Lokasi Mulai", "Jarak ke Site (m)", "Via"
            };
            double[] widths = { 6, 12, 14, 10, 10, 11, 10, 12, 18, 12, 30, 24, 24, 18, 12, 22, 12, 8 };
            const int headerRow = 4;
            var nowUtc = DateTime.UtcNow;

            ws.Cell(1, 1).Value = "Riwayat Aktivitas Kebugaran";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {periode}   |   {filterText}   |   Diunduh: {_clock.LocalNow:dd/MM/yyyy HH:mm} {zona}";
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
            if (terpotong)
            {
                ws.Cell(3, 1).Value = $"⚠ Data dipotong: hanya {MaxExportRows:N0} baris pertama yang disertakan. Persempit rentang tanggal untuk data lengkap.";
                ws.Cell(3, 1).Style.Font.FontColor = warnFont;
                ws.Cell(3, 1).Style.Font.Bold = true;
            }

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(headerRow, c + 1).Value = headers[c];
                ws.Column(c + 1).Width = widths[c];
            }
            StyleHeader(ws.Range(headerRow, 1, headerRow, headers.Length), headerFill);
            ws.Row(headerRow).Height = 30;

            var r = headerRow + 1;
            var no = 1;
            var noCheckoutFill = XLColor.FromHtml("#FFF3CD");
            foreach (var g in rows)
            {
                var local = _clock.ToLocal(g.CreatedAt);
                var state = GymRules.GetState(g, nowUtc);
                var selesai = state == GymRules.SessionState.Finished;
                ws.Cell(r, 1).Value = no++;
                ws.Cell(r, 2).Value = local.Date;
                ws.Cell(r, 3).Value = g.Aktivitas;
                ws.Cell(r, 4).Value = local.ToString("HH:mm");
                ws.Cell(r, 5).Value = g.CheckOutAt.HasValue ? _clock.ToLocal(g.CheckOutAt.Value).ToString("HH:mm") : string.Empty;
                if (g.DurasiMenit is int durasi)
                    ws.Cell(r, 6).Value = durasi;
                if (selesai && g.JarakTempuhMeter is int jarakTempuh)
                    ws.Cell(r, 7).Value = Math.Round(jarakTempuh / 1000m, 2);
                if (selesai && g.DurasiDetik is int detik && g.JarakTempuhMeter is int m && m >= 100)
                    ws.Cell(r, 8).Value = Math.Round(detik / 60m / (m / 1000m), 2);
                ws.Cell(r, 9).Value = GymRules.StatusLabel(state);
                ws.Cell(r, 10).Value = g.NIP ?? string.Empty;   // teks, supaya NIK tidak berubah jadi angka
                ws.Cell(r, 11).Value = g.NamaSnapshot;
                ws.Cell(r, 12).Value = g.DepartmentSnapshot;
                ws.Cell(r, 13).Value = g.SectionSnapshot;
                ws.Cell(r, 14).Value = g.PositionSnapshot;
                if (g.BeratBadan is decimal berat)
                    ws.Cell(r, 15).Value = berat;
                ws.Cell(r, 16).Value = g.LokasiNama ?? string.Empty;
                if (g.JarakMeter is int jarak)
                    ws.Cell(r, 17).Value = jarak;
                ws.Cell(r, 18).Value = g.LokasiVia ?? string.Empty;
                if (state == GymRules.SessionState.NoCheckOut)
                    ws.Cell(r, 9).Style.Fill.BackgroundColor = noCheckoutFill;
                r++;
            }

            var lastRow = Math.Max(r - 1, headerRow);
            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(7).Style.NumberFormat.Format = "0.00";
            ws.Column(8).Style.NumberFormat.Format = "0.00";
            ws.Column(10).Style.NumberFormat.Format = "@";
            ws.Column(15).Style.NumberFormat.Format = "0.0";
            ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(headerRow);

            // ================= Sheet 2: Ringkasan =================
            var sum = wb.Worksheets.Add("Ringkasan");
            sum.Column(1).Width = 30;
            for (int c = 2; c <= 6; c++) sum.Column(c).Width = 16;

            sum.Cell(1, 1).Value = "Ringkasan Aktivitas Kebugaran";
            sum.Cell(1, 1).Style.Font.Bold = true;
            sum.Cell(1, 1).Style.Font.FontSize = 14;
            sum.Cell(2, 1).Value = $"Periode: {periode}";
            sum.Cell(3, 1).Value = filterText;
            sum.Range(2, 1, 3, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");

            var durasiList = rows.Where(x => x.DurasiMenit.HasValue).Select(x => x.DurasiMenit!.Value).ToList();
            sum.Cell(5, 1).Value = "Total sesi aktivitas";
            sum.Cell(5, 2).Value = rows.Count;
            sum.Cell(6, 1).Value = "Karyawan berbeda";
            sum.Cell(6, 2).Value = rows.Select(x => x.EmployeeId).Distinct().Count();
            sum.Cell(7, 1).Value = "Rata-rata durasi (menit)";
            if (durasiList.Count > 0)
                sum.Cell(7, 2).Value = Math.Round(durasiList.Average(), 0);
            else
                sum.Cell(7, 2).Value = "-";
            sum.Cell(8, 1).Value = "Tidak diselesaikan";
            sum.Cell(8, 2).Value = rows.Count(x => GymRules.GetState(x, nowUtc) == GymRules.SessionState.NoCheckOut);
            sum.Cell(9, 1).Value = "Total jarak GPS (km)";
            sum.Cell(9, 2).Value = Math.Round(rows.Where(x => x.CheckOutStatus == GymCheckOutStatus.Manual)
                .Sum(x => (long)(x.JarakTempuhMeter ?? 0)) / 1000m, 2);
            sum.Range(5, 1, 9, 1).Style.Font.Bold = true;

            var next = WriteBreakdown(sum, 11, "Per Aktivitas", rows.GroupBy(x => x.Aktivitas), headerFill);
            next = WriteBreakdown(sum, next + 2, "Per Department", rows.GroupBy(x => x.DepartmentSnapshot), headerFill);
            WriteBreakdown(sum, next + 2, "Per Jabatan", rows.GroupBy(x => x.PositionSnapshot), headerFill);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        // Tabel rekap: Nama | Sesi | Karyawan | Rata-rata Berat | Rata-rata Durasi | Total Jarak. Mengembalikan baris terakhir yang terisi.
        private static int WriteBreakdown(IXLWorksheet ws, int startRow, string title,
            IEnumerable<IGrouping<string, GymAttendance>> groups, XLColor headerFill)
        {
            string[] headers = { title, "Sesi", "Karyawan", "Rata-rata Berat (kg)", "Rata-rata Durasi (menit)", "Total Jarak (km)" };
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(startRow, c + 1).Value = headers[c];
            StyleHeader(ws.Range(startRow, 1, startRow, headers.Length), headerFill);

            var r = startRow + 1;
            foreach (var g in groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(g.Key) ? "(kosong)" : g.Key;
                ws.Cell(r, 2).Value = g.Count();
                ws.Cell(r, 3).Value = g.Select(x => x.EmployeeId).Distinct().Count();
                var berat = g.Where(x => x.BeratBadan.HasValue).Select(x => x.BeratBadan!.Value).ToList();
                if (berat.Count > 0)
                {
                    ws.Cell(r, 4).Value = Math.Round(berat.Average(), 1);
                    ws.Cell(r, 4).Style.NumberFormat.Format = "0.0";
                }
                else
                    ws.Cell(r, 4).Value = "-";
                var durasi = g.Where(x => x.DurasiMenit.HasValue).Select(x => x.DurasiMenit!.Value).ToList();
                if (durasi.Count > 0)
                    ws.Cell(r, 5).Value = Math.Round(durasi.Average(), 0);
                else
                    ws.Cell(r, 5).Value = "-";
                var jarak = g.Where(x => x.CheckOutStatus == GymCheckOutStatus.Manual).Sum(x => (long)(x.JarakTempuhMeter ?? 0));
                if (jarak > 0)
                {
                    ws.Cell(r, 6).Value = Math.Round(jarak / 1000m, 2);
                    ws.Cell(r, 6).Style.NumberFormat.Format = "0.00";
                }
                else
                    ws.Cell(r, 6).Value = "-";
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

        private static void StyleHeader(IXLRange range, XLColor fill)
        {
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = fill;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            range.Style.Alignment.WrapText = true;
        }

        // Dipakai bersama oleh Data (tabel) dan Export (Excel) supaya filternya selalu sama.
        private GymPageQuery BuildQuery(DateTime? from, DateTime? to, string? department, string? position, string? status,
            int? type, string? search, int sortColumn, string? sortDir) => new()
        {
            Search = search,
            Status = status,
            ActivityTypeId = type,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Department = department,
            Position = position,
            SortColumn = sortColumn,
            SortDesc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
        };

        private static string? StatusFilterLabel(string? status) => status?.ToLowerInvariant() switch
        {
            "ingym" => "Berlangsung",
            "selesai" => "Selesai",
            "tidak" => "Tidak diselesaikan",
            "lama" => "Data lama",
            _ => null
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
