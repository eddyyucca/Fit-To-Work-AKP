using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    /// <summary>Status satu baris import pemkes.</summary>
    public static class CheckupImportStatus
    {
        public const string Siap = "Siap";            // akan disimpan
        public const string Dilewati = "Dilewati";    // sudah ada pemkes karyawan ini di tanggal yang sama
        public const string Gagal = "Gagal";          // ada isian salah, tidak disimpan

        public static string BadgeClass(string s) => s switch
        {
            Siap => "bg-success",
            Dilewati => "bg-warning text-dark",
            _ => "bg-danger"
        };
    }

    /// <summary>Satu kolom template: kolom tetap atau parameter pemkes.</summary>
    public record CheckupImportColumn(string Header, string Kind, int? ParameterId = null, string? Note = null);

    /// <summary>Satu baris Excel setelah dibaca (belum dicocokkan dengan karyawan & belum dinilai).</summary>
    public class CheckupImportRow
    {
        public int RowNumber { get; set; }
        public string Nip { get; set; } = string.Empty;
        public string? NamaDiFile { get; set; }
        public DateTime? ExaminedAtLocal { get; set; }

        /// <summary>Key = Id parameter. Angka (format 123.4) untuk Number, Id kriteria untuk Choice — sama seperti form input.</summary>
        public Dictionary<int, string?> Values { get; set; } = new();
        public decimal? BeratBadan { get; set; }
        public decimal? TinggiBadan { get; set; }
        public string? Keluhan { get; set; }
        public string? Catatan { get; set; }
        public string? Temuan { get; set; }
        public DateTime? FollowUpDate { get; set; }

        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        // Diisi setelah dicocokkan & dinilai (pratinjau)
        public int? EmployeeId { get; set; }
        public string? Nama { get; set; }
        public string? Department { get; set; }
        public int? TotalSkor { get; set; }
        public string? RisikoLabel { get; set; }
        public string? RisikoColor { get; set; }
        public List<string> Abnormal { get; set; } = new();       // ringkasan nilai di luar normal (untuk pratinjau)
        public string Status { get; set; } = CheckupImportStatus.Siap;

        public bool AdaTemuan => !string.IsNullOrWhiteSpace(Temuan);
    }

    public class CheckupImportFile
    {
        public string FileName { get; set; } = string.Empty;
        public List<CheckupImportRow> Rows { get; set; } = new();
        public List<string> FileErrors { get; set; } = new();
        public List<string> FileWarnings { get; set; } = new();
    }

    /// <summary>
    /// Template & pembacaan file Excel pemeriksaan kesehatan (pemkes) untuk import massal.
    /// Kolom parameter dibuat dari menu Parameter &amp; Skor Pemkes (yang aktif), jadi template ikut berubah kalau parameter diubah.
    /// Umur & jenis kelamin TIDAK ada di template: diambil dari Data Karyawan berdasarkan NIP (sama seperti form input).
    /// Kolom dibaca berdasarkan judulnya (bukan urutan), jadi urutan kolom boleh berubah.
    /// </summary>
    public static class CheckupImport
    {
        public const string SheetName = "Pemkes";
        public const int MaxRows = 1000;

        // Jenis kolom tetap
        public const string KNip = "nip", KNama = "nama", KTanggal = "tanggal", KBerat = "berat", KTinggi = "tinggi",
            KKeluhan = "keluhan", KCatatan = "catatan", KTemuan = "temuan", KFollowUp = "followup", KParam = "param";

        public const string HNip = "NIP";
        public const string HNama = "Nama Karyawan";
        public const string HTanggal = "Tanggal Pemeriksaan";
        public const string HBerat = "Berat Badan (kg)";
        public const string HTinggi = "Tinggi Badan (cm)";
        public const string HKeluhan = "Keluhan";
        public const string HCatatan = "Catatan";
        public const string HTemuan = "Temuan / Rencana Follow Up";
        public const string HFollowUp = "Tanggal Follow Up";

        /// <summary>Jam yang dipakai kalau kolom tanggal hanya berisi tanggal (tanpa jam).</summary>
        public static readonly TimeSpan DefaultTime = new(8, 0, 0);

        public static string ParameterHeader(CheckupParameter p) =>
            string.IsNullOrWhiteSpace(p.Unit) || p.InputType == CheckupInputTypes.Choice ? p.Name : $"{p.Name} ({p.Unit})";

        /// <summary>Daftar kolom template sesuai parameter aktif saat ini.</summary>
        public static List<CheckupImportColumn> BuildColumns(IEnumerable<CheckupParameter> parameters)
        {
            var cols = new List<CheckupImportColumn>
            {
                new(HNip, KNip, Note: "Wajib. NIP karyawan AKP (umur & jenis kelamin diambil dari Data Karyawan)."),
                new(HNama, KNama, Note: "Opsional, hanya untuk dicek. Yang dipakai tetap nama di Data Karyawan."),
                new(HTanggal, KTanggal, Note: "Wajib. dd/mm/yyyy atau dd/mm/yyyy HH:mm. Tanpa jam = 08:00.")
            };
            var bmiAdded = false;
            foreach (var p in parameters.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
            {
                switch (p.InputType)
                {
                    case CheckupInputTypes.Number:
                        cols.Add(new(ParameterHeader(p), KParam, p.Id, RangeNote(p)));
                        break;
                    case CheckupInputTypes.Choice:
                        cols.Add(new(ParameterHeader(p), KParam, p.Id,
                            "Pilih dari daftar: " + string.Join(" / ", p.Criteria.OrderBy(c => c.SortOrder).Select(c => c.Label))));
                        break;
                    case CheckupInputTypes.Bmi when !bmiAdded:
                        // IMT dihitung dari berat & tinggi (sama seperti form input)
                        cols.Add(new(HBerat, KBerat, Note: $"Berat 20 – 300 kg. Isi berat DAN tinggi untuk menghitung {p.Name}."));
                        cols.Add(new(HTinggi, KTinggi, Note: "Tinggi 100 – 230 cm."));
                        bmiAdded = true;
                        break;
                    // Age & Sex: otomatis dari Data Karyawan, tidak ada kolom
                }
            }
            cols.Add(new(HKeluhan, KKeluhan, Note: "Opsional, maks. 1000 karakter."));
            cols.Add(new(HCatatan, KCatatan, Note: "Opsional, maks. 1000 karakter."));
            cols.Add(new(HTemuan, KTemuan, Note: "Opsional. Diisi = ada temuan, wajib isi Tanggal Follow Up juga."));
            cols.Add(new(HFollowUp, KFollowUp, Note: "dd/mm/yyyy. Wajib kalau kolom Temuan diisi."));
            return cols;
        }

        private static string? RangeNote(CheckupParameter p)
        {
            var unit = string.IsNullOrWhiteSpace(p.Unit) ? "" : " " + p.Unit;
            var range = p.MinInput.HasValue || p.MaxInput.HasValue
                ? $"Angka {(p.MinInput.HasValue ? CheckupScoring.Fmt(p.MinInput) : "…")} – {(p.MaxInput.HasValue ? CheckupScoring.Fmt(p.MaxInput) : "…")}{unit}."
                : "Angka.";
            return string.IsNullOrWhiteSpace(p.Hint) ? range : $"{range} {p.Hint}";
        }

        // ======================= Template =======================

        public static byte[] BuildTemplate(IReadOnlyList<CheckupParameter> parameters, IReadOnlyList<CheckupRiskLevel> riskLevels, DateTime generatedLocal)
        {
            var cols = BuildColumns(parameters);
            var byId = parameters.ToDictionary(p => p.Id);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(SheetName);
            var lists = wb.Worksheets.Add("Pilihan");
            var listCol = 1;

            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                var col = i + 1;
                var cell = ws.Cell(1, col);
                cell.Value = c.Header;
                if (c.Note is not null)
                    cell.CreateComment().AddText(c.Note);

                var data = ws.Range(2, col, MaxRows + 1, col);
                switch (c.Kind)
                {
                    case KNip:
                    case KNama:
                        data.Style.NumberFormat.Format = "@";   // teks: NIP diawali 0 tidak hilang
                        break;
                    case KTanggal:
                        data.Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
                        break;
                    case KFollowUp:
                        data.Style.NumberFormat.Format = "dd/mm/yyyy";
                        break;
                    case KBerat:
                        data.CreateDataValidation().Decimal.Between(20, 300);
                        break;
                    case KTinggi:
                        data.CreateDataValidation().Decimal.Between(100, 230);
                        break;
                    case KParam when c.ParameterId is int pid && byId.TryGetValue(pid, out var p):
                        if (p.InputType == CheckupInputTypes.Choice && p.Criteria.Count > 0)
                        {
                            // Daftar pilihan ditulis di sheet "Pilihan" lalu dipakai sebagai dropdown
                            var labels = p.Criteria.OrderBy(x => x.SortOrder).Select(x => x.Label).ToList();
                            lists.Cell(1, listCol).Value = p.Name;
                            for (int k = 0; k < labels.Count; k++)
                                lists.Cell(k + 2, listCol).Value = labels[k];
                            var dv = data.CreateDataValidation();
                            dv.List(lists.Range(2, listCol, labels.Count + 1, listCol), true);
                            dv.IgnoreBlanks = true;
                            listCol++;
                        }
                        else if (p.InputType == CheckupInputTypes.Number && (p.MinInput.HasValue || p.MaxInput.HasValue))
                        {
                            data.CreateDataValidation().Decimal.Between((double)(p.MinInput ?? -1000000m), (double)(p.MaxInput ?? 1000000m));
                        }
                        break;
                }
            }

            var head = ws.Range(1, 1, 1, cols.Count);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            head.Style.Alignment.WrapText = true;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 32;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#C00000");   // kolom wajib
            ws.Cell(1, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#C00000");
            for (int i = 0; i < cols.Count; i++)
                ws.Column(i + 1).Width = Math.Clamp(cols[i].Header.Length + 4, 12, 30);
            ws.Column(2).Width = 28;
            ws.SheetView.FreezeRows(1);
            ws.SheetView.FreezeColumns(2);

            if (listCol == 1)
                lists.Cell(1, 1).Value = "(tidak ada parameter pilihan)";
            lists.Columns().AdjustToContents();
            lists.Visibility = XLWorksheetVisibility.Hidden;

            // Petunjuk
            var help = wb.Worksheets.Add("Petunjuk");
            var lines = new List<string>
            {
                "Cara import hasil pemeriksaan kesehatan (pemkes):",
                "1. Isi satu baris per karyawan per pemeriksaan di sheet \"Pemkes\". Kolom merah (NIP, Tanggal Pemeriksaan) wajib.",
                "2. Karyawan dicocokkan berdasarkan NIP. Umur & jenis kelamin diambil otomatis dari Data Karyawan (lengkapi di sana kalau masih kosong).",
                "3. Skor & kategori risiko DIHITUNG SISTEM — tidak perlu (dan tidak bisa) diisi di file ini.",
                "4. IMT dihitung dari Berat Badan & Tinggi Badan: isi keduanya.",
                "5. Kolom pilihan (mis. Status Merokok) pakai dropdown. Singkatan juga dikenali, mis. \"Aktif\" = Perokok aktif, \"≥150\" = ≥ 150 menit.",
                "6. Kosongkan kolom yang tidak diperiksa. Minimal satu hasil pemeriksaan atau keluhan per baris.",
                "7. Karyawan yang sudah punya pemkes di tanggal yang sama akan DILEWATI (mencegah data dobel kalau file di-upload 2x).",
                "8. Setelah upload, sistem menampilkan pratinjau. Data baru tersimpan setelah Anda klik Simpan. Baris yang gagal dilewati, baris lain tetap disimpan.",
                $"9. Template ini mengikuti Parameter & Skor Pemkes per {generatedLocal:dd/MM/yyyy HH:mm}. Kalau parameter diubah, unduh template baru.",
                "",
                "Kategori risiko (total skor):"
            };
            lines.AddRange(riskLevels.OrderBy(r => r.SortOrder).ThenBy(r => r.MinScore)
                .Select(r => $"   • {r.Label}: {r.MinScore}{(r.MaxScore.HasValue ? " – " + r.MaxScore : " ke atas")}"));
            for (int i = 0; i < lines.Count; i++)
                help.Cell(i + 1, 1).Value = lines[i];
            help.Cell(1, 1).Style.Font.Bold = true;
            help.Column(1).Width = 120;

            ws.SetTabActive();
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        // ======================= Baca file =======================

        /// <summary>Baca isi file (format & isian per sel). Pencocokan NIP, cek dobel & skor dilakukan setelahnya.</summary>
        public static CheckupImportFile Read(Stream file, string fileName, IReadOnlyList<CheckupParameter> parameters)
        {
            var result = new CheckupImportFile { FileName = fileName };
            XLWorkbook wb;
            try
            {
                wb = new XLWorkbook(file);
            }
            catch (Exception)
            {
                result.FileErrors.Add("File tidak bisa dibaca. Pastikan formatnya .xlsx (Excel), bukan .xls / .csv.");
                return result;
            }

            using (wb)
            {
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);
                var expected = BuildColumns(parameters);
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                // Petakan kolom berdasarkan judul (huruf besar/kecil & spasi diabaikan)
                var map = new Dictionary<int, CheckupImportColumn>();
                var unknown = new List<string>();
                for (int c = 1; c <= lastCol; c++)
                {
                    var header = ws.Cell(1, c).GetString();
                    if (string.IsNullOrWhiteSpace(header))
                        continue;
                    var col = expected.FirstOrDefault(e => HeaderKey(e.Header) == HeaderKey(header));
                    if (col is null)
                        unknown.Add(header.Trim());
                    else if (!map.ContainsValue(col))
                        map[c] = col;
                }

                if (!map.Values.Any(c => c.Kind == KNip) || !map.Values.Any(c => c.Kind == KTanggal))
                {
                    result.FileErrors.Add($"Kolom \"{HNip}\" dan \"{HTanggal}\" wajib ada di baris 1. Gunakan template dari tombol \"Unduh Template\".");
                    return result;
                }

                var missing = expected.Where(e => e.Kind is KParam or KBerat or KTinggi && !map.ContainsValue(e)).Select(e => e.Header).ToList();
                if (missing.Count > 0)
                    result.FileWarnings.Add("Kolom berikut tidak ada di file, jadi tidak diisi: " + string.Join(", ", missing)
                        + ". Kalau parameter baru saja diubah, unduh template terbaru.");
                if (unknown.Count > 0)
                    result.FileWarnings.Add("Kolom tidak dikenal diabaikan: " + string.Join(", ", unknown)
                        + " (skor & kategori selalu dihitung sistem).");

                var byId = parameters.ToDictionary(p => p.Id);
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                var dataRows = 0;

                for (int r = 2; r <= lastRow; r++)
                {
                    if (map.Keys.All(c => ws.Cell(r, c).IsEmpty() || string.IsNullOrWhiteSpace(ws.Cell(r, c).GetString())))
                        continue;   // baris kosong

                    if (++dataRows > MaxRows)
                    {
                        result.FileErrors.Add($"Maksimal {MaxRows} baris sekali import. Pecah file menjadi beberapa bagian.");
                        result.Rows.Clear();
                        return result;
                    }

                    var row = new CheckupImportRow { RowNumber = r };
                    foreach (var (c, col) in map)
                        ReadCell(ws.Cell(r, c), col, row, byId);

                    if (string.IsNullOrEmpty(row.Nip))
                        row.Errors.Insert(0, "NIP kosong");
                    if (row.BeratBadan.HasValue != row.TinggiBadan.HasValue)
                        row.Errors.Add("Isi berat dan tinggi badan sekaligus untuk menghitung IMT");
                    if (row.AdaTemuan && row.FollowUpDate is null && !row.Errors.Any(e => e.StartsWith(HFollowUp)))
                        row.Errors.Add("Temuan diisi: Tanggal Follow Up wajib diisi");
                    if (!row.AdaTemuan && row.FollowUpDate.HasValue)
                        row.Warnings.Add("Tanggal Follow Up diabaikan karena kolom Temuan kosong");

                    result.Rows.Add(row);
                }

                if (result.Rows.Count == 0)
                    result.FileErrors.Add("Tidak ada baris data (mulai baris 2).");
            }
            return result;
        }

        private static void ReadCell(IXLCell cell, CheckupImportColumn col, CheckupImportRow row, IReadOnlyDictionary<int, CheckupParameter> byId)
        {
            var text = CellText(cell);
            switch (col.Kind)
            {
                case KNip:
                    row.Nip = text ?? string.Empty;
                    if (row.Nip.Length > 20)
                        row.Errors.Add("NIP maksimal 20 karakter");
                    break;
                case KNama:
                    row.NamaDiFile = text;
                    break;
                case KTanggal:
                {
                    var (v, err) = ReadDateTime(cell, text, withTime: true);
                    if (err is not null) row.Errors.Add($"{HTanggal}: {err}");
                    else if (v is null) row.Errors.Add($"{HTanggal} kosong");
                    row.ExaminedAtLocal = v;
                    break;
                }
                case KFollowUp:
                {
                    var (v, err) = ReadDateTime(cell, text, withTime: false);
                    if (err is not null) row.Errors.Add($"{HFollowUp}: {err}");
                    row.FollowUpDate = v?.Date;
                    break;
                }
                case KBerat:
                case KTinggi:
                {
                    if (text is null) break;
                    var (n, err) = ParseNumber(cell, text);
                    var (min, max, name) = col.Kind == KBerat ? (20m, 300m, "Berat badan") : (100m, 230m, "Tinggi badan");
                    if (err is not null) { row.Errors.Add($"{name}: \"{text}\" bukan angka"); break; }
                    var v = Math.Round(n!.Value, 1, MidpointRounding.AwayFromZero);
                    if (v < min || v > max) { row.Errors.Add($"{name} {CheckupScoring.Fmt(v)} di luar {min:0}–{max:0}"); break; }
                    if (col.Kind == KBerat) row.BeratBadan = v; else row.TinggiBadan = v;
                    break;
                }
                case KKeluhan:
                case KCatatan:
                case KTemuan:
                {
                    if (text is not null && text.Length > 1000)
                    {
                        row.Warnings.Add($"{col.Header} dipotong menjadi 1000 karakter");
                        text = text[..1000];
                    }
                    if (col.Kind == KKeluhan) row.Keluhan = text;
                    else if (col.Kind == KCatatan) row.Catatan = text;
                    else row.Temuan = text;
                    break;
                }
                case KParam when col.ParameterId is int pid && byId.TryGetValue(pid, out var p):
                {
                    if (text is null) break;
                    if (p.InputType == CheckupInputTypes.Choice)
                    {
                        var match = MatchChoice(p, text);
                        if (match is null)
                            row.Errors.Add($"{p.Name}: \"{text}\" tidak dikenal (pilih: {string.Join(" / ", p.Criteria.OrderBy(c => c.SortOrder).Select(c => c.Label))})");
                        else
                            row.Values[p.Id] = match.Id.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        var (n, err) = ParseNumber(cell, text);
                        if (err is not null) row.Errors.Add($"{p.Name}: \"{text}\" bukan angka");
                        else row.Values[p.Id] = n!.Value.ToString(CultureInfo.InvariantCulture);   // rentang dicek oleh CheckupScoring
                    }
                    break;
                }
            }
        }

        // ======================= Helper (logika murni, bisa di-unit-test) =======================

        /// <summary>Judul kolom dibandingkan tanpa huruf besar/kecil, spasi & tanda baca.</summary>
        public static string HeaderKey(string s) =>
            new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>
        /// Cocokkan isian pilihan dengan label kriteria. Urutan: sama persis → label diawali isian / isian diawali label → label memuat isian.
        /// Spasi, huruf besar/kecil, kata "menit" dan jenis tanda minus diabaikan. Hasil harus tepat satu, kalau tidak = null.
        /// Contoh: "Aktif" → "Perokok aktif", "≥150" → "≥ 150 menit", "Tidak" → "Tidak" (Diabetes) / "Tidak pernah merokok".
        /// </summary>
        public static CheckupCriterion? MatchChoice(CheckupParameter p, string raw)
        {
            var key = ChoiceKey(raw);
            if (key.Length == 0)
                return null;
            var options = p.Criteria.Select(c => (c, k: ChoiceKey(c.Label))).Where(x => x.k.Length > 0).ToList();

            var exact = options.Where(o => o.k == key).ToList();
            if (exact.Count == 1) return exact[0].c;

            var prefix = options.Where(o => o.k.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(o.k, StringComparison.Ordinal)).ToList();
            if (prefix.Count == 1) return prefix[0].c;

            var contains = options.Where(o => o.k.Contains(key, StringComparison.Ordinal)).ToList();
            return contains.Count == 1 ? contains[0].c : null;
        }

        public static string ChoiceKey(string s)
        {
            var t = s.ToLowerInvariant()
                .Replace("–", "-").Replace("—", "-").Replace("−", "-")
                .Replace(">=", "≥").Replace("<=", "≤")
                .Replace("menit", "");
            var sb = new StringBuilder();
            foreach (var ch in t)
                if (!char.IsWhiteSpace(ch) && ch != '.' && ch != ',')
                    sb.Append(ch);
            return sb.ToString();
        }

        private static string? CellText(IXLCell cell)
        {
            if (cell.IsEmpty())
                return null;
            var s = cell.GetString().Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Angka dari sel Excel. Sel angka dibaca apa adanya; teks boleh pakai koma desimal ("5,6").</summary>
        public static (decimal? Value, string? Error) ParseNumber(IXLCell cell, string text)
        {
            if (cell.DataType == XLDataType.Number)
                return ((decimal)cell.GetDouble(), null);
            return ParseNumberText(text);
        }

        public static (decimal? Value, string? Error) ParseNumberText(string text)
        {
            var t = text.Trim();
            // Satu koma tanpa titik = koma desimal (format Indonesia). Selain itu harus format angka biasa.
            if (t.Count(ch => ch == ',') == 1 && !t.Contains('.'))
                t = t.Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                ? (n, null)
                : (null, "bukan angka");
        }

        private static readonly string[] DateFormats =
        {
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd",
            "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm", "d-M-yyyy H:mm", "dd-MM-yyyy HH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm"
        };

        public static (DateTime? Value, string? Error) ReadDateTime(IXLCell cell, string? text, bool withTime)
        {
            if (text is null && cell.IsEmpty())
                return (null, null);

            DateTime? v = null;
            if (cell.DataType == XLDataType.DateTime)
                v = cell.GetDateTime();
            else if (cell.DataType == XLDataType.Number)
            {
                try { v = DateTime.FromOADate(cell.GetDouble()); }
                catch { return (null, "tanggal tidak valid"); }
            }
            else if (text is not null)
            {
                var (parsed, err) = ParseDateText(text);
                if (err is not null) return (null, err);
                v = parsed;
            }

            if (v is null)
                return (null, null);
            if (v.Value.Year < 2000 || v.Value.Year > 2100)
                return (null, "tanggal tidak valid (gunakan dd/mm/yyyy)");

            if (!withTime)
                return (v.Value.Date, null);
            // Tanpa jam (tepat 00:00) = pakai jam default
            var dt = v.Value.TimeOfDay == TimeSpan.Zero ? v.Value.Date + DefaultTime : new DateTime(v.Value.Year, v.Value.Month, v.Value.Day, v.Value.Hour, v.Value.Minute, 0);
            return (dt, null);
        }

        public static (DateTime? Value, string? Error) ParseDateText(string text) =>
            DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? (d, null)
                : (null, $"\"{text.Trim()}\" bukan tanggal (gunakan dd/mm/yyyy)");
    }
}
