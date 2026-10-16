using System.Globalization;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    /// <summary>Isian form pemkes yang akan dinilai.</summary>
    public class CheckupScoreInput
    {
        /// <summary>Key = Id parameter. Angka (format 123.4) untuk tipe Number, Id kriteria untuk tipe Choice.</summary>
        public IDictionary<int, string?> Values { get; set; } = new Dictionary<int, string?>();
        public decimal? BeratBadan { get; set; }
        public decimal? TinggiBadan { get; set; }
        public DateTime? TanggalLahir { get; set; }
        public string? JenisKelamin { get; set; }      // L / P
        public DateTime TanggalPemeriksaan { get; set; } // tanggal lokal site
    }

    public class CheckupScoreResult
    {
        public List<HealthCheckupValue> Values { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public int Total { get; set; }
        public int ScoredFilled { get; set; }
        public int ScoredTotal { get; set; }
        public CheckupRiskLevel? Risk { get; set; }
    }

    /// <summary>
    /// Menilai isian pemkes berdasarkan parameter & kriteria yang diatur admin (menu Parameter &amp; Skor Pemkes).
    /// Logika yang sama dijalankan di browser (wwwroot/js/checkup-form.js) hanya untuk pratinjau; yang disimpan selalu hasil server.
    /// </summary>
    public static class CheckupScoring
    {
        public static CheckupScoreResult Evaluate(IEnumerable<CheckupParameter> parameters, IEnumerable<CheckupRiskLevel> riskLevels, CheckupScoreInput input)
        {
            var result = new CheckupScoreResult();
            var sex = input.JenisKelamin is "L" or "P" ? input.JenisKelamin : null;

            foreach (var p in parameters.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
            {
                if (p.IsScored)
                    result.ScoredTotal++;

                input.Values.TryGetValue(p.Id, out var raw);
                raw = raw?.Trim();

                decimal? number = null;
                string? text = null;
                CheckupCriterion? match = null;

                switch (p.InputType)
                {
                    case CheckupInputTypes.Number:
                        if (string.IsNullOrEmpty(raw))
                            continue;
                        if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                        {
                            result.Errors.Add($"{p.Name}: \"{raw}\" bukan angka yang valid.");
                            continue;
                        }
                        n = Math.Round(n, Math.Clamp(p.Decimals, 0, 3), MidpointRounding.AwayFromZero);
                        if ((p.MinInput.HasValue && n < p.MinInput) || (p.MaxInput.HasValue && n > p.MaxInput))
                        {
                            result.Errors.Add($"{p.Name}: {Fmt(n)} di luar rentang yang masuk akal ({Fmt(p.MinInput)} – {Fmt(p.MaxInput)} {p.Unit}).");
                            continue;
                        }
                        number = n;
                        break;

                    case CheckupInputTypes.Age:
                        if (input.TanggalLahir is null)
                        {
                            result.Warnings.Add($"{p.Name}: tanggal lahir karyawan belum diisi, poin tidak dihitung.");
                            continue;
                        }
                        number = Age(input.TanggalLahir.Value, input.TanggalPemeriksaan);
                        break;

                    case CheckupInputTypes.Bmi:
                        if (input.BeratBadan is not > 0 || input.TinggiBadan is not > 0)
                            continue;
                        var m = input.TinggiBadan.Value / 100m;
                        number = Math.Round(input.BeratBadan.Value / (m * m), Math.Clamp(p.Decimals, 0, 3), MidpointRounding.AwayFromZero);
                        break;

                    case CheckupInputTypes.Sex:
                        if (sex is null)
                        {
                            result.Warnings.Add($"{p.Name}: jenis kelamin karyawan belum diisi, poin tidak dihitung.");
                            continue;
                        }
                        text = sex == "L" ? "Laki-laki" : "Perempuan";
                        match = p.Criteria.OrderBy(c => c.SortOrder).FirstOrDefault(c => c.MatchText == sex);
                        break;

                    case CheckupInputTypes.Choice:
                        if (string.IsNullOrEmpty(raw))
                            continue;
                        match = int.TryParse(raw, out var cid) ? p.Criteria.FirstOrDefault(c => c.Id == cid) : null;
                        if (match is null)
                        {
                            result.Errors.Add($"{p.Name}: pilihan tidak valid.");
                            continue;
                        }
                        text = match.Label;
                        break;

                    default:
                        continue;
                }

                if (number.HasValue)
                    match = p.Criteria.OrderBy(c => c.SortOrder).FirstOrDefault(c => c.Matches(number.Value, sex));

                var value = new HealthCheckupValue
                {
                    ParameterId = p.Id,
                    ParameterName = p.Name,
                    Unit = p.Unit,
                    InputType = p.InputType,
                    SortOrder = p.SortOrder,
                    NumericValue = number,
                    TextValue = text,
                    CriteriaLabel = match?.Label,
                    Points = match?.Points,
                    Level = match?.Level ?? CheckupLevels.None,
                    IsScored = p.IsScored
                };
                result.Values.Add(value);

                if (p.IsScored && value.Points.HasValue)
                {
                    result.Total += value.Points.Value;
                    result.ScoredFilled++;
                }
            }

            if (result.ScoredFilled > 0)
                result.Risk = riskLevels.OrderBy(r => r.SortOrder).ThenBy(r => r.MinScore).FirstOrDefault(r => r.Matches(result.Total));

            return result;
        }

        public static int Age(DateTime birth, DateTime onDate)
        {
            var age = onDate.Year - birth.Year;
            if (birth.Date > onDate.Date.AddYears(-age)) age--;
            return age;
        }

        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
        public static string Fmt(decimal? v) => v?.ToString("0.###", Id) ?? "-";
    }

    /// <summary>
    /// Baris hasil pemkes yang seragam untuk ditampilkan / di-export, baik pemeriksaan format baru
    /// (parameter custom) maupun data lama (kolom tetap, dinilai dengan <see cref="HealthRanges"/>).
    /// </summary>
    public static class CheckupDisplay
    {
        public static List<CheckupDisplayRow> Rows(HealthCheckup c) => c.UsesParameters ? NewRows(c) : LegacyRows(c);

        public static int AbnormalCount(HealthCheckup c) => Rows(c).Count(r => r.IsAbnormal);

        public static IEnumerable<CheckupDisplayRow> Abnormal(HealthCheckup c) => Rows(c).Where(r => r.IsAbnormal);

        private static List<CheckupDisplayRow> NewRows(HealthCheckup c)
        {
            var rows = new List<CheckupDisplayRow>();
            foreach (var v in c.Values.OrderBy(v => v.SortOrder).ThenBy(v => v.Id))
            {
                if (v.InputType == CheckupInputTypes.Bmi)
                {
                    // Berat & tinggi badan ditampilkan tepat sebelum IMT
                    if (c.BeratBadan.HasValue)
                        rows.Add(new CheckupDisplayRow("Berat badan", $"{CheckupScoring.Fmt(c.BeratBadan)} kg", c.BeratBadan, "kg", CheckupLevels.None, null, null, false));
                    if (c.TinggiBadan.HasValue)
                        rows.Add(new CheckupDisplayRow("Tinggi badan", $"{CheckupScoring.Fmt(c.TinggiBadan)} cm", c.TinggiBadan, "cm", CheckupLevels.None, null, null, false));
                }

                var text = v.NumericValue.HasValue
                    ? CheckupScoring.Fmt(v.NumericValue) + (string.IsNullOrEmpty(v.Unit) ? "" : " " + v.Unit)
                    : v.TextValue ?? "-";
                rows.Add(new CheckupDisplayRow(v.ParameterName, text, v.NumericValue, v.Unit, v.Level,
                    v.CriteriaLabel ?? "di luar kriteria", v.Points, v.IsScored));
            }
            return rows;
        }

        private static List<CheckupDisplayRow> LegacyRows(HealthCheckup c)
        {
            var f = HealthRanges.Evaluate(c);
            var rows = new List<CheckupDisplayRow>();
            void Add(string label, decimal? number, string? text, string? unit, HealthFlag flag)
            {
                if (number is null && text is null) return;
                rows.Add(new CheckupDisplayRow(label, text ?? $"{CheckupScoring.Fmt(number)}{(unit is null ? "" : " " + unit)}",
                    number, unit, MapLevel(flag.Level), string.IsNullOrEmpty(flag.Label) ? null : flag.Label, null, false));
            }

            Add("Tekanan darah", null, c.TekananDarah is null ? null : c.TekananDarah + " mmHg", "mmHg", f["TekananDarah"]);
            Add("Berat badan", c.BeratBadan, null, "kg", HealthFlag.None);
            Add("Tinggi badan", c.TinggiBadan, null, "cm", HealthFlag.None);
            Add("IMT", c.Imt, null, null, f["Imt"]);
            Add("Lingkar perut", c.LingkarPerut, null, "cm", f["LingkarPerut"]);
            Add("Kolesterol total", c.Kolesterol, null, "mg/dL", f["Kolesterol"]);
            Add("Asam urat", c.AsamUrat, null, "mg/dL", f["AsamUrat"]);
            Add($"Gula darah ({GulaDarahTypes.Label(c.GulaDarahJenis).ToLowerInvariant()})", c.GulaDarah, null, "mg/dL", f["GulaDarah"]);
            return rows;
        }

        private static string MapLevel(HealthLevel l) => l switch
        {
            HealthLevel.Normal => CheckupLevels.Normal,
            HealthLevel.Perhatian => CheckupLevels.Perhatian,
            HealthLevel.Tinggi => CheckupLevels.Risiko,
            HealthLevel.Rendah => CheckupLevels.Info,
            _ => CheckupLevels.None
        };
    }
}
