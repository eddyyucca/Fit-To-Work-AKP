namespace FitToWorkApp.Models
{
    public class FormOption
    {
        public int Id { get; set; }
        public string GroupKey { get; set; } = string.Empty;
        public string OptionValue { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // Daftar "group" pilihan yang dipakai form Fit To Work.
    // Tambahkan konstanta baru di sini kalau suatu saat ada pertanyaan baru yang butuh dropdown/radio.
    public static class FormOptionGroups
    {
        public const string JadwalKerja = "JadwalKerja";
        public const string AreaKerja = "AreaKerja";
        public const string JamTidur = "JamTidur";
        public const string KualitasTidur = "KualitasTidur";
        public const string GejalaKesehatan = "GejalaKesehatan";
        public const string KondisiFokus = "KondisiFokus";
        public const string KesiapanKerja = "KesiapanKerja";

        // Label yang ditampilkan di halaman admin, supaya lebih mudah dibaca daripada raw key.
        public static readonly Dictionary<string, string> Labels = new()
        {
            [JadwalKerja] = "Jadwal Kerja",
            [AreaKerja] = "Area Kerja",
            [JamTidur] = "Jam Tidur",
            [KualitasTidur] = "Kualitas Tidur",
            [GejalaKesehatan] = "Gejala Kesehatan",
            [KondisiFokus] = "Kondisi Fokus & Pikiran",
            [KesiapanKerja] = "Kesiapan Kerja"
        };

        public static readonly string[] All =
        {
            JadwalKerja, AreaKerja, JamTidur, KualitasTidur, GejalaKesehatan, KondisiFokus, KesiapanKerja
        };
    }
}
