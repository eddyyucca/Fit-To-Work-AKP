using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitToWorkApp.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AdminController : Controller
    {
        private const int TrendDays = 20;
        private const int TopCount = 10;
        private const int RecentCount = 10;

        private readonly IDashboardRepository _dashboardRepository;
        private readonly IDashboardExtraRepository _extraRepository;
        private readonly IAppClock _clock;
        private readonly IPermissionService _permissionService;

        public AdminController(IDashboardRepository dashboardRepository, IDashboardExtraRepository extraRepository,
            IAppClock clock, IPermissionService permissionService)
        {
            _dashboardRepository = dashboardRepository;
            _extraRepository = extraRepository;
            _clock = clock;
            _permissionService = permissionService;
        }

        // Halaman awal setelah login admin: arahkan ke menu pertama yang boleh diakses role-nya.
        [HttpGet]
        public async Task<IActionResult> Home()
        {
            var access = await _permissionService.GetAsync(User);
            // Dashboard sekarang berisi bagian FTW, Klinik & Gym -> jadi halaman awal kalau boleh melihat salah satunya
            if (access.Has(Permissions.FtwDashboard) || access.Has(Permissions.GymHistory) || access.Has(Permissions.MedicalVisit))
                return RedirectToAction(nameof(Dashboard));

            foreach (var (permission, controller, action) in Permissions.LandingOrder)
            {
                if (access.Has(permission))
                    return RedirectToAction(action, controller);
            }
            return RedirectToAction("AccessDenied", "Account");
        }

        // Halaman dashboard. Semua angka & chart diisi lewat JavaScript dari DashboardData,
        // lalu diperbarui otomatis via SignalR setiap ada karyawan yang submit.
        // Bagian yang tampil mengikuti hak akses: FTW (ftw.dashboard), Klinik (medical.visit), Gym (gym.history)
        [HttpGet]
        [HasPermission(Permissions.FtwDashboard, Permissions.MedicalVisit, Permissions.GymHistory)]
        public async Task<IActionResult> Dashboard()
        {
            var access = await _permissionService.GetAsync(User);
            ViewBag.ShowFtw = access.Has(Permissions.FtwDashboard);
            ViewBag.ShowClinic = access.Has(Permissions.MedicalVisit);
            ViewBag.ShowGym = access.Has(Permissions.GymHistory);
            ViewBag.TrendDays = TrendDays;
            ViewBag.TimeZoneId = _clock.TimeZoneId;
            return View();
        }

        // GET /Admin/DashboardData?period=day|week|month
        [HttpGet]
        [HasPermission(Permissions.FtwDashboard)]
        public async Task<IActionResult> DashboardData(string period = "day")
        {
            var today = _clock.Today;
            var offset = _clock.UtcOffsetMinutes;
            var todayStartUtc = _clock.ToUtc(today);
            var tomorrowStartUtc = _clock.ToUtc(today.AddDays(1));

            // 1. Ringkasan hari ini
            var summary = await _dashboardRepository.GetTodaySummaryAsync(todayStartUtc, tomorrowStartUtc);

            // 2. Tren 20 hari (tanggal tanpa pengisian tetap muncul dengan nilai 0)
            var trendStart = today.AddDays(-(TrendDays - 1));
            var trendStartUtc = _clock.ToUtc(trendStart);
            var trendRows = (await _dashboardRepository.GetDailyTrendAsync(trendStartUtc, tomorrowStartUtc, offset))
                .ToDictionary(r => r.Tanggal.Date);

            var trend = Enumerable.Range(0, TrendDays)
                .Select(i =>
                {
                    var date = trendStart.AddDays(i);
                    trendRows.TryGetValue(date, out var r);
                    return new
                    {
                        tanggal = date.ToString("yyyy-MM-dd"),
                        label = date.ToString("dd/MM"),
                        total = r?.Total ?? 0,
                        nonShift = r?.NonShift ?? 0,
                        shiftPagi = r?.ShiftPagi ?? 0,
                        shiftMalam = r?.ShiftMalam ?? 0,
                        fit = r?.Fit ?? 0,
                        tidakFit = r?.TidakFit ?? 0
                    };
                })
                .ToList();

            // 3. Doughnut: karyawan vs yang sudah isi (hari / minggu / bulan)
            var coverage = await BuildCoverageAsync(period, today, tomorrowStartUtc, offset);

            // 4. Top 10 register dalam 20 hari terakhir
            var top = (await _dashboardRepository.GetTopRegistrantsAsync(trendStartUtc, tomorrowStartUtc, offset, TopCount))
                .Select((t, i) => new
                {
                    rank = i + 1,
                    nip = t.NIP,
                    nama = t.Nama,
                    jabatan = string.IsNullOrWhiteSpace(t.Jabatan) ? "-" : t.Jabatan,
                    section = string.IsNullOrWhiteSpace(t.Section) ? "-" : t.Section,
                    jumlahHari = t.JumlahHari,
                    tidakFit = t.TidakFit,
                    terakhir = _clock.ToLocal(t.TerakhirUtc).ToString("dd/MM HH:mm")
                });

            // 5. Pengisian terbaru hari ini
            var recent = (await _dashboardRepository.GetRecentAsync(todayStartUtc, tomorrowStartUtc, RecentCount))
                .Select(r => DashboardSubmissionEvent.From(r, _clock.ToLocal(r.CreatedAtUtc)));

            return Json(new
            {
                generatedAt = _clock.LocalNow.ToString("HH:mm:ss"),
                // Untuk jam realtime di browser: waktu server (UTC, milidetik) + offset zona waktu site,
                // supaya jam yang tampil tetap WITA/WIB walau jam komputer admin tidak tepat.
                serverTimeUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                utcOffsetMinutes = offset,
                zonaWaktu = offset switch { 420 => "WIB", 480 => "WITA", 540 => "WIT", _ => _clock.TimeZoneId },
                todayKey = today.ToString("yyyy-MM-dd"),
                today = new
                {
                    tanggal = FormatTanggalIndonesia(today),
                    totalKaryawan = summary.TotalKaryawan,
                    sudahIsi = summary.SudahIsi,
                    belumIsi = summary.BelumIsi,
                    fit = summary.Fit,
                    tidakFit = summary.TidakFit,
                    persen = Percent(summary.SudahIsi, summary.TotalKaryawan)
                },
                trend,
                coverage,
                top,
                recent
            });
        }

        // ---------------- Dashboard Klinik ----------------

        // GET /Admin/DashboardClinic?period=day|week|month
        [HttpGet]
        [HasPermission(Permissions.MedicalVisit)]
        public async Task<IActionResult> DashboardClinic(string period = "day")
        {
            var r = ResolvePeriod(period);
            var fromUtc = _clock.ToUtc(r.Start);
            var toUtc = _clock.ToUtc(_clock.Today.AddDays(1));
            var offset = _clock.UtcOffsetMinutes;
            var hourly = r.Period == "day";

            var summary = await _extraRepository.GetClinicSummaryAsync(fromUtc, toUtc);
            var trendRows = (await _extraRepository.GetClinicTrendAsync(fromUtc, toUtc, offset, hourly, r.Start)).ToDictionary(x => x.Bucket);
            var days = (_clock.Today - r.Start).Days + 1;

            var trend = hourly
                ? Enumerable.Range(0, 24).Select(h => new { label = $"{h:00}:00", jumlah = trendRows.TryGetValue(h, out var x) ? x.Jumlah : 0 })
                : Enumerable.Range(0, days).Select(i => new { label = r.Start.AddDays(i).ToString("dd/MM"), jumlah = trendRows.TryGetValue(i, out var x) ? x.Jumlah : 0 });

            return Json(new
            {
                period = r.Period,
                label = r.Label,
                summary,
                trend,
                topDiseases = await _extraRepository.GetTopDiseasesAsync(fromUtc, toUtc, TopCount),
                topDepartments = await _extraRepository.GetTopClinicDepartmentsAsync(fromUtc, toUtc, TopCount)
            });
        }

        // ---------------- Dashboard Aktivitas Kebugaran (dulu Gym) ----------------

        // GET /Admin/DashboardGym?period=day|week|month
        [HttpGet]
        [HasPermission(Permissions.GymHistory)]
        public async Task<IActionResult> DashboardGym(string period = "day")
        {
            var r = ResolvePeriod(period);
            var fromUtc = _clock.ToUtc(r.Start);
            var toUtc = _clock.ToUtc(_clock.Today.AddDays(1));

            var summary = await _extraRepository.GetGymSummaryAsync(fromUtc, toUtc);
            var perHourRows = (await _extraRepository.GetGymPerHourAsync(fromUtc, toUtc, _clock.UtcOffsetMinutes)).ToDictionary(x => x.Bucket);
            var perHour = Enumerable.Range(0, 24)
                .Select(h => new { jam = h, label = $"{h:00}:00", jumlah = perHourRows.TryGetValue(h, out var x) ? x.Jumlah : 0 })
                .ToList();
            var peak = perHour.Where(p => p.jumlah > 0).OrderByDescending(p => p.jumlah).ThenBy(p => p.jam).FirstOrDefault();

            var top = (await _extraRepository.GetGymTopAsync(fromUtc, toUtc, TopCount)).Select((t, i) => new
            {
                rank = i + 1,
                nip = t.NIP,
                nama = t.Nama,
                department = string.IsNullOrWhiteSpace(t.Department) ? "-" : t.Department,
                jumlah = t.Jumlah,
                totalDurasi = t.TotalMenit is int menit && menit > 0 ? GymRules.FormatDuration(menit) : null,
                totalJarak = t.TotalJarakMeter is long jarak && jarak > 0 ? GymRules.FormatDistance((int)Math.Min(jarak, int.MaxValue)) : null,
                terakhir = _clock.ToLocal(t.TerakhirUtc).ToString("dd/MM HH:mm"),
                beratTerakhir = t.BeratTerakhir
            });

            var perActivity = (await _extraRepository.GetGymPerActivityAsync(fromUtc, toUtc)).Select(a => new
            {
                nama = a.Nama,
                ikon = a.Ikon,
                jumlah = a.Jumlah,
                karyawan = a.Karyawan,
                rataDurasi = a.RataDurasiMenit is decimal rd ? GymRules.FormatDuration((int)Math.Round(rd)) : null,
                totalJarak = a.TotalJarakMeter > 0 ? GymRules.FormatDistance((int)Math.Min(a.TotalJarakMeter, int.MaxValue)) : null
            });

            return Json(new
            {
                period = r.Period,
                label = r.Label,
                totalKunjungan = summary.TotalKunjungan,
                karyawanUnik = summary.KaryawanUnik,
                totalKaryawan = summary.TotalKaryawanAktif,
                belumKeGym = Math.Max(summary.TotalKaryawanAktif - summary.KaryawanUnik, 0),
                persen = Percent(summary.KaryawanUnik, summary.TotalKaryawanAktif),
                rataKunjungan = summary.KaryawanUnik == 0 ? 0 : Math.Round((decimal)summary.TotalKunjungan / summary.KaryawanUnik, 1),
                rataDurasi = summary.RataDurasiMenit is decimal d ? GymRules.FormatDuration((int)Math.Round(d)) : null,
                tidakCheckout = summary.TidakCheckout,
                totalJarak = summary.TotalJarakMeter > 0 ? GymRules.FormatDistance((int)Math.Min(summary.TotalJarakMeter, int.MaxValue)) : null,
                sedangDiGym = summary.SedangDiGym,
                perActivity,
                jamTersibuk = peak is null ? null : $"{peak.jam:00}:00 - {peak.jam + 1:00}:00",
                jamTersibukJumlah = peak?.jumlah ?? 0,
                perHour,
                top
            });
        }

        // Periode sama dengan dashboard FTW: hari ini / minggu berjalan (mulai Senin) / bulan berjalan
        private (string Period, DateTime Start, string Label) ResolvePeriod(string? period)
        {
            var today = _clock.Today;
            switch ((period ?? "day").ToLowerInvariant())
            {
                case "week":
                    var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    return ("week", monday, $"Minggu ini ({monday:dd/MM} - {today:dd/MM})");
                case "month":
                    var first = new DateTime(today.Year, today.Month, 1);
                    return ("month", first, $"Bulan ini ({first:dd/MM} - {today:dd/MM})");
                default:
                    return ("day", today, $"Hari ini ({today:dd/MM/yyyy})");
            }
        }

        private async Task<object> BuildCoverageAsync(string period, DateTime today, DateTime tomorrowStartUtc, int offset)
        {
            // Minggu = minggu berjalan mulai Senin; Bulan = bulan berjalan (lihat ResolvePeriod)
            (period, var start, var label) = ResolvePeriod(period, today);

            var row = await _dashboardRepository.GetCoverageAsync(_clock.ToUtc(start), tomorrowStartUtc, offset);

            // Harian: target = jumlah karyawan aktif.
            // Mingguan/bulanan: target = karyawan aktif x hari operasional (hari yang ada pengisiannya).
            var hari = period == "day" ? 1 : Math.Max(row.HariOperasional, 1);
            var target = row.TotalKaryawan * hari;
            var sudah = Math.Min(row.SudahIsi, target);

            return new
            {
                period,
                label,
                totalKaryawan = row.TotalKaryawan,
                hariOperasional = hari,
                target,
                sudahIsi = sudah,
                belumIsi = Math.Max(target - sudah, 0),
                persen = Percent(sudah, target)
            };
        }

        // ---------------- Dashboard FTW: Pengisian per Section ----------------

        // GET /Admin/DashboardSection?period=day|week|month
        // Per section: jumlah karyawan vs yang sudah isi + achievement %. Default urut dari achievement terendah.
        [HttpGet]
        [HasPermission(Permissions.FtwDashboard)]
        public async Task<IActionResult> DashboardSection(string period = "day")
        {
            var today = _clock.Today;
            var offset = _clock.UtcOffsetMinutes;
            var tomorrowStartUtc = _clock.ToUtc(today.AddDays(1));
            var (p, start, label) = ResolvePeriod(period, today);
            var fromUtc = _clock.ToUtc(start);

            var hari = await HariOperasionalAsync(p, fromUtc, tomorrowStartUtc, offset);
            var rows = (await _dashboardRepository.GetSectionAchievementAsync(fromUtc, tomorrowStartUtc, offset)).ToList();

            var items = rows
                .Select(r =>
                {
                    var a = FtwSectionRules.Compute(r.JumlahKaryawan, r.SudahIsi, hari);
                    return new
                    {
                        sectionId = r.SectionId ?? 0,   // 0 = (Tanpa Section)
                        section = r.SectionId is null ? "(Tanpa Section)" : r.Section ?? "-",
                        department = string.IsNullOrWhiteSpace(r.Department) ? "-" : r.Department,
                        jumlahKaryawan = r.JumlahKaryawan,
                        karyawanPernahIsi = r.KaryawanPernahIsi,
                        target = a.Target,
                        sudahIsi = a.SudahIsi,
                        belumIsi = a.BelumIsi,
                        fit = r.Fit,
                        tidakFit = r.TidakFit,
                        persen = a.Persen,
                        level = FtwSectionRules.LevelKey(a.Level)
                    };
                })
                .OrderBy(x => x.persen).ThenBy(x => x.department).ThenBy(x => x.section)
                .ToList();

            var total = FtwSectionRules.Compute(rows.Sum(r => r.JumlahKaryawan), rows.Sum(r => r.SudahIsi), hari);

            return Json(new
            {
                period = p,
                label,
                hariOperasional = hari,
                batasHijau = FtwSectionRules.BatasHijau,
                batasKuning = FtwSectionRules.BatasKuning,
                items,
                total = new
                {
                    jumlahKaryawan = rows.Sum(r => r.JumlahKaryawan),
                    karyawanPernahIsi = rows.Sum(r => r.KaryawanPernahIsi),
                    target = total.Target,
                    sudahIsi = total.SudahIsi,
                    belumIsi = total.BelumIsi,
                    fit = rows.Sum(r => r.Fit),
                    tidakFit = rows.Sum(r => r.TidakFit),
                    persen = total.Persen,
                    level = FtwSectionRules.LevelKey(total.Level)
                }
            });
        }

        // GET /Admin/DashboardSectionDetail?period=day&sectionId=5  (sectionId 0 = Tanpa Section)
        // Daftar karyawan section itu yang BELUM isi (harian) / belum lengkap (mingguan & bulanan).
        [HttpGet]
        [HasPermission(Permissions.FtwDashboard)]
        public async Task<IActionResult> DashboardSectionDetail(int sectionId, string period = "day")
        {
            var today = _clock.Today;
            var offset = _clock.UtcOffsetMinutes;
            var tomorrowStartUtc = _clock.ToUtc(today.AddDays(1));
            var (p, start, label) = ResolvePeriod(period, today);
            var fromUtc = _clock.ToUtc(start);

            var hari = await HariOperasionalAsync(p, fromUtc, tomorrowStartUtc, offset);
            var all = (await _dashboardRepository.GetSectionEmployeesAsync(fromUtc, tomorrowStartUtc, offset,
                sectionId > 0 ? sectionId : null)).ToList();

            var items = all
                .Where(e => e.HariIsi < hari)
                .OrderBy(e => e.HariIsi).ThenBy(e => e.Nama)
                .Select(e => new
                {
                    nip = e.NIP,
                    nama = e.Nama,
                    jabatan = string.IsNullOrWhiteSpace(e.Jabatan) ? "-" : e.Jabatan,
                    hariIsi = e.HariIsi,
                    terakhir = e.TerakhirUtc.HasValue ? _clock.ToLocal(e.TerakhirUtc.Value).ToString("dd/MM/yyyy HH:mm") : "Belum pernah"
                })
                .ToList();

            return Json(new
            {
                period = p,
                label,
                hariOperasional = hari,
                jumlahKaryawan = all.Count,
                items
            });
        }

        // Harian = 1 hari. Mingguan/bulanan = hari yang ada minimal 1 pengisian (sama dengan doughnut cakupan).
        private async Task<int> HariOperasionalAsync(string period, DateTime fromUtc, DateTime toUtc, int offset)
        {
            if (period == "day")
                return 1;
            var row = await _dashboardRepository.GetCoverageAsync(fromUtc, toUtc, offset);
            return Math.Max(row.HariOperasional, 1);
        }

        // Rentang periode dashboard FTW: hari ini / minggu berjalan (mulai Senin) / bulan berjalan
        private static (string Period, DateTime Start, string Label) ResolvePeriod(string? period, DateTime today)
        {
            switch ((period ?? "day").ToLowerInvariant())
            {
                case "week":
                    var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    return ("week", monday, $"Minggu ini ({monday:dd/MM} - {today:dd/MM})");
                case "month":
                    var first = new DateTime(today.Year, today.Month, 1);
                    return ("month", first, $"Bulan ini ({first:dd/MM} - {today:dd/MM})");
                default:
                    return ("day", today, $"Hari ini ({today:dd/MM/yyyy})");
            }
        }

        // Format manual (bukan CultureInfo "id-ID") supaya tetap benar walau image Docker
        // berjalan dengan InvariantGlobalization / tanpa data ICU.
        private static readonly string[] NamaHari = { "Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" };
        private static readonly string[] NamaBulan = { "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };

        private static string FormatTanggalIndonesia(DateTime d) =>
            $"{NamaHari[(int)d.DayOfWeek]}, {d.Day:00} {NamaBulan[d.Month - 1]} {d.Year}";

        private static decimal Percent(int part, int total) =>
            total <= 0 ? 0 : Math.Round(part * 100m / total, 1);
    }
}
