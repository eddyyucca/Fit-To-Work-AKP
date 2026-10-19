namespace FitToWorkApp.Models
{
    public static class FtwFollowUpKanal
    {
        public const string WhatsApp = "WhatsApp";
        public const string Telepon = "Telepon";
        public const string Pesan = "Pesan";
        public const string Lainnya = "Lainnya";
        public static readonly string[] All = { WhatsApp, Telepon, Pesan, Lainnya };
        public static string Label(string k) => k switch { Pesan => "Pesan / SMS", _ => k };
    }

    /// <summary>Hasil menghubungi karyawan yang tidak fit (follow up online).</summary>
    public static class FtwFollowUpHasil
    {
        public const string Membaik = "Membaik";
        public const string MasihSakit = "MasihSakit";
        public const string PerluKlinik = "PerluKlinik";
        public const string TidakDapatDihubungi = "TidakDapatDihubungi";
        public const string IstirahatDiRumah = "IstirahatDiRumah";
        public const string Lainnya = "Lainnya";
        public static readonly string[] All = { Membaik, IstirahatDiRumah, MasihSakit, PerluKlinik, TidakDapatDihubungi, Lainnya };

        public static string Label(string? h) => h switch
        {
            Membaik => "Sudah membaik",
            IstirahatDiRumah => "Istirahat di mess",
            MasihSakit => "Masih sakit",
            PerluKlinik => "Perlu datang ke klinik",
            TidakDapatDihubungi => "Tidak dapat dihubungi",
            Lainnya => "Lainnya",
            _ => h ?? "-"
        };

        public static string BadgeClass(string? h) => h switch
        {
            Membaik => "bg-success",
            IstirahatDiRumah => "bg-info text-dark",
            MasihSakit => "bg-warning text-dark",
            PerluKlinik => "bg-danger",
            TidakDapatDihubungi => "bg-secondary",
            _ => "bg-secondary"
        };
    }

    /// <summary>Satu kali follow up online terhadap satu kejadian tidak fit. BUKAN kunjungan klinik.</summary>
    public class FtwFollowUpLog
    {
        public int Id { get; set; }
        public int FtwSubmissionId { get; set; }
        public string Kanal { get; set; } = FtwFollowUpKanal.WhatsApp;
        public string Hasil { get; set; } = FtwFollowUpHasil.Membaik;
        public string? Catatan { get; set; }
        public DateTime FollowUpAt { get; set; }      // UTC
        public bool IsClosed { get; set; }            // true = masalah selesai, kasus ditutup
        public DateTime? NextFollowUpDate { get; set; }   // jadwal follow up berikutnya (tanggal lokal) untuk kasus yang belum selesai
        public int? ByAdminId { get; set; }
        public string ByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
