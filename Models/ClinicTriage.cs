using System.Globalization;

namespace FitToWorkApp.Models
{
    public static class TriageStatuses
    {
        public const string Draft = "Draft";        // dicatat perawat, menunggu dokter
        public const string Selesai = "Selesai";    // difinalkan dokter menjadi Kunjungan Klinik
        public const string Batal = "Batal";
        public static readonly string[] All = { Draft, Selesai, Batal };

        public static string Label(string s) => s switch
        {
            Draft => "Draft - menunggu dokter",
            Selesai => "Selesai (sudah difinalkan dokter)",
            Batal => "Dibatalkan",
            _ => s
        };

        public static string BadgeClass(string s) => s switch
        {
            Draft => "bg-warning text-dark",
            Selesai => "bg-success",
            Batal => "bg-secondary",
            _ => "bg-secondary"
        };
    }

    /// <summary>
    /// Pemeriksaan awal oleh perawat (keluhan + tanda vital), disimpan sebagai draft sampai dokter melanjutkannya
    /// di form Kunjungan Klinik dan memfinalkannya. Draft BUKAN kunjungan klinik: tidak masuk dashboard/laporan.
    /// </summary>
    public class ClinicTriage
    {
        private static readonly CultureInfo IdCulture = CultureInfo.GetCultureInfo("id-ID");

        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int? FtwSubmissionId { get; set; }        // pengisian FTW tidak fit yang menjadi asal pemeriksaan awal ini (opsional)
        public string NamaSnapshot { get; set; } = string.Empty;
        public string DepartmentSnapshot { get; set; } = string.Empty;
        public string SectionSnapshot { get; set; } = string.Empty;
        public string PositionSnapshot { get; set; } = string.Empty;
        public DateTime TriageAt { get; set; }           // UTC
        public string Keluhan { get; set; } = string.Empty;
        public int? Sistolik { get; set; }
        public int? Diastolik { get; set; }
        public int? Nadi { get; set; }
        public decimal? Suhu { get; set; }
        public int? Napas { get; set; }
        public int? Spo2 { get; set; }
        public decimal? BeratBadan { get; set; }
        public string? CatatanPerawat { get; set; }
        public string Status { get; set; } = TriageStatuses.Draft;
        public int? NurseAdminId { get; set; }
        public string NurseName { get; set; } = string.Empty;
        public int? VisitId { get; set; }
        public DateTime? FinalizedAt { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelledBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Diisi lewat JOIN (bukan kolom tabel)
        public string? NIP { get; set; }

        public bool IsDraft => Status == TriageStatuses.Draft;

        /// <summary>Ringkasan tanda vital satu baris, mis. "TD 120/80 mmHg, Nadi 80 x/mnt, Suhu 36,8 °C". Kosong kalau tidak ada.</summary>
        public string VitalSummary()
        {
            var parts = new List<string>();
            if (Sistolik.HasValue || Diastolik.HasValue)
                parts.Add($"TD {Sistolik?.ToString() ?? "?"}/{Diastolik?.ToString() ?? "?"} mmHg");
            if (Nadi.HasValue) parts.Add($"Nadi {Nadi} x/mnt");
            if (Suhu.HasValue) parts.Add($"Suhu {Suhu.Value.ToString("0.0", IdCulture)} °C");
            if (Napas.HasValue) parts.Add($"RR {Napas} x/mnt");
            if (Spo2.HasValue) parts.Add($"SpO2 {Spo2}%");
            if (BeratBadan.HasValue) parts.Add($"BB {BeratBadan.Value.ToString("0.#", IdCulture)} kg");
            return string.Join(", ", parts);
        }

        /// <summary>Teks awal isian "Hasil Pemeriksaan" di form dokter (masih bisa diedit dokter). Maksimal 1000 karakter.</summary>
        public string BuildPemeriksaanText(DateTime triageLocal)
        {
            var lines = new List<string>
            {
                $"Pemeriksaan awal ({NurseName}, {triageLocal:dd/MM/yyyy HH:mm})" + (VitalSummary() is { Length: > 0 } v ? ": " + v : "")
            };
            if (!string.IsNullOrWhiteSpace(CatatanPerawat))
                lines.Add("Catatan perawat: " + CatatanPerawat.Trim());
            var text = string.Join("\n", lines);
            return text.Length <= 1000 ? text : text[..1000];
        }
    }
}
