namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>
    /// Satu baris laporan "Hasil Screening FTW &amp; Pemeriksaan": jawaban Fit To Work karyawan yang tidak fit
    /// digabung dengan hasil pemeriksaan klinik (kalau ada). Baca-saja.
    /// </summary>
    public class FtwScreeningRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JadwalKerja { get; set; } = string.Empty;
        public string AreaKerja { get; set; } = string.Empty;
        public string JamTidur { get; set; } = string.Empty;
        public string KualitasTidur { get; set; } = string.Empty;
        public string GejalaKesehatan { get; set; } = string.Empty;
        public string KondisiFokus { get; set; } = string.Empty;
        public string KonsumsiObat { get; set; } = string.Empty;
        public DateTime WaktuUtc { get; set; }

        // Dari kunjungan klinik dalam 48 jam setelah mengisi (hanya kalau akun boleh melihat data klinik)
        public DateTime? KlinikUtc { get; set; }
        public string? Diagnosa { get; set; }
        public string? Pemeriksaan { get; set; }
        public string? Obat { get; set; }
        public string? CatatanRekomendasi { get; set; }
        public string? Rekomendasi { get; set; }
        public int? HariIstirahat { get; set; }
        public DateTime? IstirahatDari { get; set; }
        public DateTime? IstirahatSampai { get; set; }

        // Follow up online terbaru (bukan kunjungan klinik)
        public int? OnlineId { get; set; }
        public string? OnlineHasil { get; set; }
        public bool? OnlineClosed { get; set; }
        public DateTime? OnlineNext { get; set; }

        // Diisi controller
        public DateTime Tanggal { get; set; }                       // tanggal lokal site saat mengisi
        public string HasilPemeriksaan { get; set; } = string.Empty;
        public string Tindakan { get; set; } = string.Empty;
        public string RekomendasiText { get; set; } = string.Empty;
        public bool Diperiksa => KlinikUtc is not null;
        public bool Close { get; set; }                             // kasus selesai: diperiksa klinik atau follow up online ditutup
        public string StatusKasus { get; set; } = string.Empty;     // teks status kasus; diawali "Selesai" atau "Belum close"
        public string StatusDetail { get; set; } = string.Empty;
    }

    public class FtwScreeningViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Department { get; set; }
        public string Scope { get; set; } = "all";       // all = semua yang tidak fit; diperiksa = hanya yang sudah diperiksa klinik
        public List<string> Departments { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public bool CanSeeClinic { get; set; }
        public bool Truncated { get; set; }
        public int Total { get; set; }
        public int Diperiksa { get; set; }
        public int BelumClose { get; set; }
        public List<FtwScreeningRow> Rows { get; set; } = new();
    }
}
