using FitToWorkApp.Hubs;
using FitToWorkApp.Models.ViewModels;
using FitToWorkApp.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace FitToWorkApp.Services
{
    /// <summary>
    /// Mengirim notifikasi realtime (SignalR) ke Dashboard admin yang sedang terbuka.
    /// Setiap event hanya dikirim ke grup yang berhak (lihat <see cref="DashboardHub"/>).
    /// Gagal kirim tidak pernah menggagalkan proses utama (submit karyawan, simpan kunjungan, dll.).
    /// </summary>
    public interface IDashboardNotifier
    {
        /// <summary>Submit Fit To Work baru (dengan data untuk toast di dashboard).</summary>
        Task NotifySubmissionAsync(int submissionId);

        /// <summary>Aktivitas kebugaran dimulai / diselesaikan.</summary>
        /// <param name="aksi">mis. "mulai Lari" atau "selesai Gym"</param>
        Task NotifyGymAsync(string nama, string aksi = "check-in");

        /// <summary>Kunjungan klinik baru / dibatalkan.</summary>
        Task NotifyClinicChangedAsync();

        /// <summary>Data karyawan berubah atau data direset: semua bagian dashboard dimuat ulang.</summary>
        Task NotifyRefreshAllAsync();
    }

    public class DashboardNotifier : IDashboardNotifier
    {
        private readonly IHubContext<DashboardHub> _hub;
        private readonly IDashboardRepository _dashboardRepository;
        private readonly IAppClock _clock;
        private readonly ILogger<DashboardNotifier> _logger;

        public DashboardNotifier(
            IHubContext<DashboardHub> hub,
            IDashboardRepository dashboardRepository,
            IAppClock clock,
            ILogger<DashboardNotifier> logger)
        {
            _hub = hub;
            _dashboardRepository = dashboardRepository;
            _clock = clock;
            _logger = logger;
        }

        public async Task NotifySubmissionAsync(int submissionId)
        {
            // Notifikasi dashboard tidak boleh menggagalkan submit karyawan,
            // jadi error apa pun di sini cukup di-log.
            try
            {
                var row = await _dashboardRepository.GetSubmissionAsync(submissionId);
                if (row is null) return;

                var payload = DashboardSubmissionEvent.From(row, _clock.ToLocal(row.CreatedAtUtc));
                await _hub.Clients.Group(DashboardHub.GroupFtw).SendAsync(DashboardHub.RegisteredEvent, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengirim notifikasi dashboard untuk submission {SubmissionId}", submissionId);
            }
        }

        public Task NotifyGymAsync(string nama, string aksi = "check-in") =>
            SafeSendAsync(new[] { DashboardHub.GroupGym }, DashboardHub.GymEvent,
                new { nama, aksi, jam = _clock.ToLocal(DateTime.UtcNow).ToString("HH:mm") });

        // Tanpa detail pasien: cukup sinyal supaya dashboard memuat ulang ringkasan
        public Task NotifyClinicChangedAsync() =>
            SafeSendAsync(new[] { DashboardHub.GroupClinic }, DashboardHub.ClinicEvent, new { });

        public Task NotifyRefreshAllAsync() =>
            SafeSendAsync(DashboardHub.AllGroups, DashboardHub.RefreshEvent, new { });

        private async Task SafeSendAsync(IReadOnlyList<string> groups, string eventName, object payload)
        {
            try
            {
                await _hub.Clients.Groups(groups).SendAsync(eventName, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengirim notifikasi dashboard {Event}", eventName);
            }
        }
    }
}
