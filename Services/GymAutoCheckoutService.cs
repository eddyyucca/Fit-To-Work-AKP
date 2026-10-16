using FitToWorkApp.Repositories;

namespace FitToWorkApp.Services
{
    /// <summary>
    /// Setiap 5 menit menutup sesi aktivitas kebugaran yang lewat batas jam jenis aktivitasnya (kolom BatasAt)
    /// (status 'Auto' = "Tidak diselesaikan", durasi & jarak tidak dihitung). Tampilan riwayat & dashboard juga sudah menganggap
    /// sesi seperti itu "Tidak check-out", jadi jeda sampai 5 menit ini tidak mengubah angka yang tampil.
    /// </summary>
    public class GymAutoCheckoutService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GymAutoCheckoutService> _logger;

        public GymAutoCheckoutService(IServiceScopeFactory scopeFactory, ILogger<GymAutoCheckoutService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IGymRepository>();
                    var closed = await repo.CloseStaleAsync();
                    if (closed > 0)
                    {
                        _logger.LogInformation("Aktivitas: {Count} sesi yang tidak diselesaikan ditutup otomatis", closed);
                        var notifier = scope.ServiceProvider.GetRequiredService<IDashboardNotifier>();
                        await notifier.NotifyRefreshAllAsync();
                    }
                }
                catch (Exception ex)
                {
                    // Mis. migration_fitness_activity.sql belum dijalankan / database sedang tidak bisa diakses
                    _logger.LogWarning(ex, "Aktivitas: gagal menutup sesi yang tidak diselesaikan");
                }

                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
