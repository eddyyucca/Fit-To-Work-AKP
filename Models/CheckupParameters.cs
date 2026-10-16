namespace FitToWorkApp.Models
{
    // ======================= Parameter & skor pemeriksaan kesehatan (custom) =======================

    /// <summary>Jenis isian parameter pemkes.</summary>
    public static class CheckupInputTypes
    {
        public const string Number = "Number";   // angka diketik medic (mg/dL, mmHg, ...)
        public const string Choice = "Choice";   // pilih salah satu kriteria (Ya/Tidak, perokok, ...)
        public const string Age = "Age";         // otomatis: umur dari tanggal lahir karyawan saat pemeriksaan
        public const string Sex = "Sex";         // otomatis: jenis kelamin dari data karyawan
        public const string Bmi = "Bmi";         // otomatis: IMT dari berat & tinggi badan yang diinput

        public static readonly string[] All = { Number, Choice, Age, Sex, Bmi };

        public static string Label(string t) => t switch
        {
            Number => "Angka",
            Choice => "Pilihan",
            Age => "Umur (otomatis dari tanggal lahir)",
            Sex => "Jenis kelamin (otomatis)",
            Bmi => "IMT (dihitung dari BB & TB)",
            _ => t
        };

        /// <summary>Tipe yang kriterianya berupa rentang angka.</summary>
        public static bool IsNumeric(string t) => t is Number or Age or Bmi;
    }

    /// <summary>Warna penanda per kriteria.</summary>
    public static class CheckupLevels
    {
        public const string None = "";
        public const string Normal = "Normal";
        public const string Perhatian = "Perhatian";
        public const string Risiko = "Risiko";
        public const string Info = "Info";

        public static readonly string[] All = { None, Normal, Perhatian, Risiko, Info };

        public static string Label(string? l) => l switch
        {
            Normal => "Normal (hijau)",
            Perhatian => "Perhatian (kuning)",
            Risiko => "Risiko (merah)",
            Info => "Info (biru)",
            _ => "Tanpa warna"
        };

        public static string BadgeClass(string? l) => l switch
        {
            Normal => "bg-success",
            Perhatian => "bg-warning text-dark",
            Risiko => "bg-danger",
            Info => "bg-info text-dark",
            _ => "bg-secondary"
        };

        /// <summary>Warna latar lembut untuk nilai di tabel.</summary>
        public static string CellClass(string? l) => l switch
        {
            Perhatian => "bg-warning-subtle text-warning-emphasis",
            Risiko => "bg-danger-subtle text-danger-emphasis fw-semibold",
            Info => "bg-info-subtle text-info-emphasis",
            _ => ""
        };

        /// <summary>Nilai yang perlu diperhatikan (di luar normal).</summary>
        public static bool IsAbnormal(string? l) => l is Perhatian or Risiko or Info;
    }

    /// <summary>Warna kategori total skor.</summary>
    public static class RiskColors
    {
        public static readonly string[] All = { "success", "warning", "orange", "danger", "info", "secondary" };

        public static string Label(string c) => c switch
        {
            "success" => "Hijau",
            "warning" => "Kuning",
            "orange" => "Oranye",
            "danger" => "Merah",
            "info" => "Biru",
            _ => "Abu-abu"
        };

        public static string BadgeClass(string? c) => c switch
        {
            "success" => "bg-success",
            "warning" => "bg-warning text-dark",
            "orange" => "bg-orange",
            "danger" => "bg-danger",
            "info" => "bg-info text-dark",
            _ => "bg-secondary"
        };
    }

    public class CheckupParameter
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string InputType { get; set; } = CheckupInputTypes.Number;
        public int Decimals { get; set; }
        public decimal? MinInput { get; set; }
        public decimal? MaxInput { get; set; }
        public bool IsScored { get; set; } = true;
        public string? Hint { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        public List<CheckupCriterion> Criteria { get; set; } = new();
        public int UsageCount { get; set; }           // jumlah nilai pemeriksaan yang memakai parameter ini

        public int MaxPoints => Criteria.Count == 0 ? 0 : Criteria.Max(c => c.Points);
    }

    public class CheckupCriterion
    {
        public int Id { get; set; }
        public int ParameterId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public string? MatchText { get; set; }        // Sex: L / P
        public string? ForSex { get; set; }           // khusus L / P, null = semua
        public int Points { get; set; }
        public string Level { get; set; } = CheckupLevels.None;
        public int SortOrder { get; set; }

        public bool Matches(decimal value, string? sex) =>
            (ForSex is null || ForSex == sex)
            && (MinValue is null || value >= MinValue)
            && (MaxValue is null || value <= MaxValue);
    }

    public class CheckupRiskLevel
    {
        public int Id { get; set; }
        public int MinScore { get; set; }
        public int? MaxScore { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = "secondary";
        public string? Description { get; set; }
        public int SortOrder { get; set; }

        public bool Matches(int score) => score >= MinScore && (MaxScore is null || score <= MaxScore);
        public string RangeLabel => MaxScore is null ? $"≥ {MinScore}" : $"{MinScore} – {MaxScore}";
    }

    /// <summary>Nilai satu parameter pada satu pemeriksaan (snapshot nama, kriteria & poin saat itu).</summary>
    public class HealthCheckupValue
    {
        public int Id { get; set; }
        public int CheckupId { get; set; }
        public int? ParameterId { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string InputType { get; set; } = CheckupInputTypes.Number;
        public int SortOrder { get; set; }
        public decimal? NumericValue { get; set; }
        public string? TextValue { get; set; }
        public string? CriteriaLabel { get; set; }
        public int? Points { get; set; }
        public string Level { get; set; } = CheckupLevels.None;
        public bool IsScored { get; set; }
    }

    /// <summary>Satu baris hasil pemkes untuk ditampilkan (format baru maupun data lama).</summary>
    public record CheckupDisplayRow(
        string Label, string ValueText, decimal? Number, string? Unit,
        string Level, string? CriteriaLabel, int? Points, bool IsScored)
    {
        public bool IsAbnormal => CheckupLevels.IsAbnormal(Level);
    }
}
