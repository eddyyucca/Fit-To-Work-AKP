namespace FitToWorkApp.Services
{
    /// <summary>
    /// Jam aplikasi berdasarkan zona waktu site (bukan zona waktu container).
    /// Kolom CreatedAt di database disimpan dalam UTC (SYSUTCDATETIME()), sedangkan container
    /// Docker berjalan di UTC. Tanpa ini, "hari ini" di dashboard baru berganti jam 08:00 WITA,
    /// sehingga pengisian shift pagi antara 00:00-07:59 masuk ke tanggal kemarin.
    ///
    /// Zona waktu diatur lewat appsettings / environment:
    ///   "App": { "TimeZone": "Asia/Makassar" }   atau   App__TimeZone=Asia/Makassar
    /// (Asia/Jakarta = WIB, Asia/Makassar = WITA, Asia/Jayapura = WIT)
    /// </summary>
    public interface IAppClock
    {
        string TimeZoneId { get; }
        DateTime LocalNow { get; }
        DateTime Today { get; }

        /// <summary>Selisih menit zona waktu site terhadap UTC (WIB=420, WITA=480).</summary>
        int UtcOffsetMinutes { get; }

        DateTime ToUtc(DateTime localTime);
        DateTime ToLocal(DateTime utcTime);
    }

    public class AppClock : IAppClock
    {
        private readonly TimeZoneInfo _timeZone;

        public AppClock(IConfiguration configuration)
        {
            TimeZoneId = configuration["App:TimeZone"] ?? "Asia/Makassar";
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }

        public string TimeZoneId { get; }

        public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        public DateTime Today => LocalNow.Date;

        public int UtcOffsetMinutes => (int)_timeZone.GetUtcOffset(DateTime.UtcNow).TotalMinutes;

        public DateTime ToUtc(DateTime localTime) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), _timeZone);

        public DateTime ToLocal(DateTime utcTime) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), _timeZone);
    }
}
