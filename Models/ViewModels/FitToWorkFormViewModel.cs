using System.ComponentModel.DataAnnotations;

namespace FitToWorkApp.Models.ViewModels
{
    public class FitToWorkFormViewModel
    {
        // ---- Data diri: prefill dari data login, ditampilkan read-only ----
        public int EmployeeId { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nomor HP/WhatsApp wajib diisi")]
        [Display(Name = "NOMOR HP / WHATSAPP")]
        public string NomorHp { get; set; } = string.Empty;

        // ---- Sisanya diisi manual sesuai form asli ----
        // Kualitas tidur, gejala, kondisi fokus & konsumsi obat hanya ditanyakan (dan wajib) kalau
        // kesiapan kerja = TIDAK FIT. Kalau FIT, field tersebut disimpan "-" (lihat FormController).
        // Catatan: daftar pilihan (radio/checkbox/dropdown) untuk field-field di bawah ini
        // TIDAK lagi hardcode di sini - diambil dari tabel dbo.FormOptions lewat
        // IFormOptionRepository, dan dikirim ke view lewat ViewBag (lihat FormController).
        [Required(ErrorMessage = "Jadwal kerja wajib dipilih")]
        [Display(Name = "JADWAL KERJA")]
        public string JadwalKerja { get; set; } = string.Empty;

        [Required(ErrorMessage = "Area kerja wajib dipilih")]
        [Display(Name = "AREA KERJA")]
        public string AreaKerja { get; set; } = string.Empty;

        [Display(Name = "Area kerja lainnya")]
        public string? AreaKerjaLainnya { get; set; }

        [Required(ErrorMessage = "Jam tidur wajib dipilih")]
        [Display(Name = "Berapa jam total waktu tidur Anda sebelum mulai bekerja?")]
        public string JamTidur { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kualitas tidur wajib dipilih")]
        [Display(Name = "Bagaimana kualitas istirahat/tidur Anda?")]
        public string KualitasTidur { get; set; } = string.Empty;

        [Display(Name = "Apakah Anda merasakan gejala kesehatan berikut hari ini?")]
        public List<string> GejalaKesehatan { get; set; } = new();

        [Required(ErrorMessage = "Kondisi fokus & pikiran wajib dipilih")]
        [Display(Name = "KONDISI FOKUS & PIKIRAN")]
        public string KondisiFokus { get; set; } = string.Empty;

        [Required(ErrorMessage = "Wajib dijawab")]
        [Display(Name = "Apakah Anda sedang mengonsumsi obat-obatan dalam 12 jam terakhir?")]
        public string KonsumsiObat { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kesiapan kerja wajib dipilih")]
        [Display(Name = "Bagaimana kesiapan Anda untuk bekerja sekarang?")]
        public string KesiapanKerja { get; set; } = string.Empty;

        /// <summary>Pilihan kesiapan yang diawali "YA" dianggap FIT (sama dengan logika dashboard & riwayat).</summary>
        public bool IsFit => KesiapanKerja.StartsWith("YA", StringComparison.OrdinalIgnoreCase);

        public const string TidakDitanyakan = "-";
    }
}
