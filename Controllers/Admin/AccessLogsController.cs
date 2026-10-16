using ClosedXML.Excel;
using FitToWorkApp.Models;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers.Admin
{
    /// <summary>Lihat log akses admin (butuh permission admin.accesslog). Log hanya bisa dibaca, tidak bisa diubah/dihapus dari UI.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.AdminAccessLog)]
    [Route("Admin/AccessLogs")]
    public class AccessLogsController : Controller
    {
        private const int DefaultRangeDays = 7;
        public const int MaxExportRows = 20000;

        private readonly IAccessLogRepository _repository;
        private readonly IAppClock _clock;

        public AccessLogsController(IAccessLogRepository repository, IAppClock clock)
        {
            _repository = repository;
            _clock = clock;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string? username = null, string? eventType = null)
        {
            var today = _clock.Today;
            ViewBag.DefaultFrom = today.AddDays(-(DefaultRangeDays - 1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = today.ToString("yyyy-MM-dd");
            ViewBag.Usernames = await _repository.GetUsernamesAsync();
            ViewBag.SelectedUsername = username;
            ViewBag.SelectedEventType = eventType;
            return View();
        }

        [HttpGet("Data")]
        public async Task<IActionResult> Data(int draw, int start, int length,
            DateTime? from, DateTime? to, string? username, string? eventType)
        {
            int.TryParse(Request.Query["order[0][column]"], out var sortColumn);
            var query = BuildQuery(from, to, username, eventType,
                Request.Query["search[value]"].ToString(), sortColumn, Request.Query["order[0][dir]"].ToString());
            query.Skip = start;
            query.Take = length;

            var page = await _repository.GetPageAsync(query);

            return Json(new
            {
                draw,
                recordsTotal = page.Total,
                recordsFiltered = page.Filtered,
                gagal = page.FilteredFailed,
                data = page.Rows.Select(l => new
                {
                    waktu = _clock.ToLocal(l.CreatedAt).ToString("dd/MM/yyyy HH:mm:ss"),
                    username = l.Username,
                    eventType = l.EventType,
                    eventLabel = AccessLogEvents.Label(l.EventType),
                    activity = l.Activity,
                    method = l.HttpMethod,
                    path = l.Path,
                    status = l.StatusCode,
                    ip = l.IpAddress,
                    userAgent = l.UserAgent
                })
            });
        }

        [HttpGet("Export")]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? username, string? eventType,
            string? search, int sort = 0, string? dir = "desc")
        {
            var query = BuildQuery(from, to, username, eventType, search, sort, dir);
            var rows = await _repository.GetForExportAsync(query, MaxExportRows + 1);
            var terpotong = rows.Count > MaxExportRows;
            if (terpotong)
                rows.RemoveAt(rows.Count - 1);

            var zona = ZonaWaktu();
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Log Akses");
            string[] headers = { "No", "Tanggal", $"Jam ({zona})", "User", "Jenis", "Aktivitas", "Method", "Path", "Status", "IP", "Browser" };
            double[] widths = { 7, 12, 10, 18, 14, 45, 8, 45, 8, 16, 45 };
            const int headerRow = 4;

            ws.Cell(1, 1).Value = "Log Akses Admin — Fit To Work";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {(from.HasValue ? from.Value.ToString("dd/MM/yyyy") : "awal")} - {(to.HasValue ? to.Value.ToString("dd/MM/yyyy") : "sekarang")}   |   Diunduh: {_clock.LocalNow:dd/MM/yyyy HH:mm} {zona}";
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6C757D");
            if (terpotong)
            {
                ws.Cell(3, 1).Value = $"⚠ Data dipotong: hanya {MaxExportRows:N0} baris pertama. Persempit rentang tanggal untuk data lengkap.";
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#842029");
            }

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(headerRow, c + 1).Value = headers[c];
                ws.Column(c + 1).Width = widths[c];
            }
            var head = ws.Range(headerRow, 1, headerRow, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");

            var warnFill = XLColor.FromHtml("#F8D7DA");
            var r = headerRow + 1;
            var no = 1;
            foreach (var l in rows)
            {
                var local = _clock.ToLocal(l.CreatedAt);
                ws.Cell(r, 1).Value = no++;
                ws.Cell(r, 2).Value = local.Date;
                ws.Cell(r, 3).Value = local.ToString("HH:mm:ss");
                ws.Cell(r, 4).Value = l.Username;
                ws.Cell(r, 5).Value = AccessLogEvents.Label(l.EventType);
                ws.Cell(r, 6).Value = l.Activity;
                ws.Cell(r, 7).Value = l.HttpMethod ?? string.Empty;
                ws.Cell(r, 8).Value = l.Path ?? string.Empty;
                if (l.StatusCode.HasValue) ws.Cell(r, 9).Value = l.StatusCode.Value;
                ws.Cell(r, 10).Value = l.IpAddress ?? string.Empty;
                ws.Cell(r, 11).Value = l.UserAgent ?? string.Empty;
                if (l.EventType is AccessLogEvents.LoginFailed or AccessLogEvents.AccessDenied or AccessLogEvents.PortalLoginFailed)
                    ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = warnFill;
                r++;
            }

            ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Range(headerRow, 1, Math.Max(r - 1, headerRow), headers.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(headerRow);

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Log_Akses_{_clock.Today:yyyyMMdd}.xlsx");
        }

        private AccessLogPageQuery BuildQuery(DateTime? from, DateTime? to, string? username, string? eventType,
            string? search, int sortColumn, string? sortDir) => new()
        {
            Search = search,
            FromUtc = from.HasValue ? _clock.ToUtc(from.Value.Date) : null,
            ToUtc = to.HasValue ? _clock.ToUtc(to.Value.Date.AddDays(1)) : null,
            Username = username,
            EventType = AccessLogEvents.All.Contains(eventType) ? eventType : null,
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
