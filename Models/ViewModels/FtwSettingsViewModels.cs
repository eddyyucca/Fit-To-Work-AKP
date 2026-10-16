using FitToWorkApp.Services;

namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Halaman karyawan "Sudah mengisi" (Views/Form/AlreadySubmitted).</summary>
    public class FtwAlreadySubmittedViewModel
    {
        public FitToWorkSubmission Submission { get; set; } = new();

        /// <summary>Jam lokal pengisian dibuka lagi.</summary>
        public DateTime NextOpenLocal { get; set; }

        /// <summary>Teks siap tampil, mis. "pukul 18:10" / "besok pukul 06:00".</summary>
        public string NextOpenText { get; set; } = string.Empty;

        public bool IzinIsiUlang { get; set; }
        public int DetikKembali { get; set; }
    }

    /// <summary>Form admin Fit To Work › Pengaturan Pengisian.</summary>
    public class FtwSettingsFormViewModel
    {
        public FtwResetMode Mode { get; set; } = FtwSettings.Default.Mode;

        /// <summary>Jeda = JedaJam jam + JedaMenit menit.</summary>
        public int? JedaJam { get; set; }
        public int? JedaMenit { get; set; }

        /// <summary>Jam reset "HH:mm" (input type=time). Baris kosong diabaikan.</summary>
        public List<string?> JamReset { get; set; } = new();

        public bool IzinIsiUlang { get; set; } = true;
        public int? DetikKembali { get; set; }

        public static FtwSettingsFormViewModel From(FtwSettings s) => new()
        {
            Mode = s.Mode,
            JedaJam = s.JedaMenit / 60,
            JedaMenit = s.JedaMenit % 60,
            JamReset = s.JamReset.OrderBy(t => t).Select(t => (string?)FtwRules.FormatTime(t)).ToList(),
            IzinIsiUlang = s.IzinIsiUlang,
            DetikKembali = s.DetikKembali
        };
    }
}
