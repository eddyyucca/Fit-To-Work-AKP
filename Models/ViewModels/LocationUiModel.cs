namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Dipakai partial _LocationStatus (form FTW, check-in & check-out gym).</summary>
    public class LocationUiModel
    {
        public const string ModeOff = "off";    // belum ada lokasi site aktif -> tidak dikunci
        public const string ModeGps = "gps";    // wajib GPS di dalam radius site
        public const string ModeWifi = "wifi";  // sudah terhubung Wi-Fi site -> GPS hanya dicatat, tidak wajib

        public string Mode { get; set; } = ModeOff;
        public string? WifiSiteName { get; set; }
    }
}
