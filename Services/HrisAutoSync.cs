namespace FitToWorkApp.Services
{
    /// <summary>Hasil sinkron HRIS terakhir (manual atau otomatis), ditampilkan di halaman Sinkron HRIS.</summary>
    public class HrisSyncStatus
    {
        private readonly object _lock = new();

        public DateTime? LastRunUtc { get; private set; }
        public DateTime? LastSuccessUtc { get; private set; }
        public string? LastTrigger { get; private set; }
        public string? LastError { get; private set; }
        public string? LastSummary { get; private set; }
        public int IntervalMinutes { get; set; }

        public void RecordSuccess(string trigger, HrisSyncResult r)
        {
            lock (_lock)
            {
                LastRunUtc = LastSuccessUtc = DateTime.UtcNow;
                LastTrigger = trigger;
                LastError = null;
                LastSummary = $"{r.FromHris} dari HRIS: {r.Created} baru, {r.Updated} diubah, {r.Unchanged} tetap, {r.Skipped} dilewati";
            }
        }

        public void RecordFailure(string trigger, string error)
        {
            lock (_lock)
            {
                LastRunUtc = DateTime.UtcNow;
                LastTrigger = trigger;
                LastError = error;
            }
        }

        public (DateTime? run, DateTime? success, string? trigger, string? error, string? summary) Snapshot()
        {
            lock (_lock) return (LastRunUtc, LastSuccessUtc, LastTrigger, LastError, LastSummary);
        }
    }

    /// <summary>
    /// Sinkron karyawan dari HRIS otomatis berkala. Interval: HRIS_SYNC_INTERVAL_MINUTES (bawaan 60; 0 = mati).
    /// Tombol "Sinkron Sekarang" di halaman admin tetap ada. Bila HRIS belum dikonfigurasi atau gagal, aplikasi tetap berjalan
    /// dan mencoba lagi pada putaran berikutnya (data lama tidak berubah karena sinkron berjalan dalam satu transaksi).
    /// </summary>
    public class HrisAutoSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HrisAutoSyncService> _logger;
        private readonly HrisSyncStatus _status;
        private readonly int _intervalMinutes;

        public HrisAutoSyncService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
            HrisSyncStatus status, ILogger<HrisAutoSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _status = status;
            _logger = logger;
            _intervalMinutes = int.TryParse(configuration["HRIS_SYNC_INTERVAL_MINUTES"], out var m) ? m : 60;
            _status.IntervalMinutes = Math.Max(_intervalMinutes, 0);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_intervalMinutes <= 0)
            {
                _logger.LogInformation("Sinkron HRIS otomatis dimatikan (HRIS_SYNC_INTERVAL_MINUTES=0)");
                return;
            }

            // Beri waktu aplikasi dan database siap sebelum putaran pertama.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sync = scope.ServiceProvider.GetRequiredService<IHrisEmployeeSync>();
                    if (sync.IsConfigured)
                    {
                        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        limit.CancelAfter(TimeSpan.FromMinutes(20));   // proses boleh lama, tapi tidak menggantung selamanya
                        var r = await sync.RunAsync(dryRun: false, limit.Token, trigger: "otomatis");
                        _logger.LogInformation("Sinkron HRIS otomatis: {From} dari HRIS, {Created} baru, {Updated} diubah, {Unchanged} tetap, {Skipped} dilewati",
                            r.FromHris, r.Created, r.Updated, r.Unchanged, r.Skipped);
                    }
                    else
                    {
                        _logger.LogWarning("Sinkron HRIS otomatis dilewati: HRIS_API_URL / HRIS_API_TOKEN belum diisi");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Sinkron HRIS otomatis gagal; dicoba lagi pada putaran berikutnya");
                }

                try { await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
