using ClosedXML.Excel;
using FitToWorkApp.Models;

namespace FitToWorkApp.Services
{
    public static class StockImportStatus
    {
        public const string Masuk = "Masuk";            // stok bertambah
        public const string Opname = "Opname";          // stok disamakan dengan hitungan fisik
        public const string ObatBaru = "ObatBaru";      // obat belum ada -> dibuat + stok awal
        public const string TanpaPerubahan = "Tetap";   // baris dibaca tapi tidak ada yang berubah
        public const string Error = "Error";

        public static string Label(string s) => s switch
        {
            Masuk => "Stok masuk",
            Opname => "Stok opname",
            ObatBaru => "Obat baru",
            TanpaPerubahan => "Tidak berubah",
            Error => "Error",
            _ => s
        };

        public static string BadgeClass(string s) => s switch
        {
            Masuk => "bg-success",
            Opname => "bg-info text-dark",
            ObatBaru => "bg-primary",
            Error => "bg-danger",
            _ => "bg-secondary"
        };
    }

    /// <summary>Satu baris file Excel stok obat setelah dibaca & dicek.</summary>
    public class StockImportRow
    {
        public int RowNumber { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string? Satuan { get; set; }
        public string Jenis { get; set; } = string.Empty;       // MASUK / OPNAME (seperti di file)
        public int? Jumlah { get; set; }
        public int? StokMinimum { get; set; }
        public string? Keterangan { get; set; }

        public int? MedicineId { get; set; }                    // null = obat baru
        public int StokLama { get; set; }
        public int StokBaru { get; set; }
        public bool UbahStokMinimum { get; set; }
        public string Status { get; set; } = StockImportStatus.TanpaPerubahan;
        public string? Pesan { get; set; }

        public int Selisih => StokBaru - StokLama;
        public bool AdaPerubahan => Status is StockImportStatus.Masuk or StockImportStatus.Opname or StockImportStatus.ObatBaru || UbahStokMinimum;
    }

    public class StockImportResult
    {
        public string FileName { get; set; } = string.Empty;
        public List<StockImportRow> Rows { get; set; } = new();
        public List<string> FileErrors { get; set; } = new();

        public bool HasErrors => FileErrors.Count > 0 || Rows.Any(r => r.Status == StockImportStatus.Error);
        public int ChangeCount => Rows.Count(r => r.Status != StockImportStatus.Error && r.AdaPerubahan);
    }

    /// <summary>
    /// Template & pembacaan file Excel untuk update stok obat sekaligus (stok masuk / stok opname / obat baru).
    /// Kolom: A Nama Obat, B Satuan, C Stok Saat Ini (info), D Jenis (MASUK/OPNAME), E Jumlah, F Stok Minimum, G Keterangan.
    /// </summary>
    public static class MedicineStockImport
    {
        public const string Masuk = "MASUK";
        public const string Opname = "OPNAME";
        public const int MaxRows = 2000;

        private static readonly string[] Headers =
            { "Nama Obat", "Satuan", "Stok Saat Ini", "Jenis (MASUK/OPNAME)", "Jumlah", "Stok Minimum", "Keterangan" };

        public static byte[] BuildTemplate(IEnumerable<Medicine> medicines, IReadOnlyList<string> satuanOptions, DateTime generatedLocal)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Stok Obat");
            for (int i = 0; i < Headers.Length; i++)
                ws.Cell(1, i + 1).Value = Headers[i];
            var head = ws.Range(1, 1, 1, Headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");

            var r = 2;
            foreach (var m in medicines.Where(m => m.IsActive).OrderBy(m => m.Name))
            {
                ws.Cell(r, 1).Value = m.Name;
                ws.Cell(r, 2).Value = m.Satuan;
                ws.Cell(r, 3).Value = m.Stok;
                ws.Cell(r, 6).Value = m.StokMinimum;
                r++;
            }
            var lastRow = Math.Max(r - 1, 1);

            // Kolom info (stok saat ini) diberi warna abu-abu: hanya referensi, tidak dibaca saat import
            ws.Range(2, 3, Math.Max(lastRow, 2), 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEEEE");
            ws.Cell(1, 3).CreateComment().AddText($"Stok di sistem saat template diunduh ({generatedLocal:dd/MM/yyyy HH:mm}). Hanya info, tidak dibaca saat import.");
            ws.Cell(1, 4).CreateComment().AddText("MASUK = stok ditambah sebanyak Jumlah (penerimaan obat).\nOPNAME = stok diganti menjadi Jumlah (hasil hitung fisik).\nKosong = dianggap MASUK.");
            ws.Cell(1, 5).CreateComment().AddText("Kosongkan untuk obat yang tidak berubah.");
            ws.Cell(1, 6).CreateComment().AddText("Opsional. Kalau diubah, stok minimum obat ikut diperbarui.");

            // Dropdown
            var dataRange = ws.Range(2, 4, MaxRows + 1, 4);
            dataRange.CreateDataValidation().List($"{Masuk},{Opname}", true);
            if (satuanOptions.Count > 0)
            {
                var ref_ = wb.Worksheets.Add("Satuan");
                ref_.Cell(1, 1).Value = "Satuan (Master Satuan Obat)";
                ref_.Cell(1, 1).Style.Font.Bold = true;
                for (int i = 0; i < satuanOptions.Count; i++)
                    ref_.Cell(i + 2, 1).Value = satuanOptions[i];
                ref_.Column(1).AdjustToContents();
                ws.Range(2, 2, MaxRows + 1, 2).CreateDataValidation().List(ref_.Range(2, 1, satuanOptions.Count + 1, 1), true);
            }
            ws.Range(2, 5, MaxRows + 1, 6).CreateDataValidation().WholeNumber.Between(0, 1000000);

            // Petunjuk
            var help = wb.Worksheets.Add("Petunjuk");
            string[] lines =
            {
                "Cara update stok obat lewat Excel:",
                "1. Isi kolom Jumlah HANYA untuk obat yang stoknya berubah. Baris dengan Jumlah kosong dilewati.",
                "2. Jenis MASUK = stok ditambah (penerimaan obat). Jenis OPNAME = stok diganti sesuai hitungan fisik.",
                "3. Obat baru: tambahkan baris di bawah, isi Nama Obat, Satuan (pilih dari dropdown) dan Jumlah sebagai stok awal.",
                "4. Jangan ubah nama obat yang sudah ada (dicocokkan berdasarkan nama). Satuan obat lama tidak diubah lewat import.",
                "5. Setelah upload, sistem menampilkan pratinjau dulu. Stok baru berubah setelah Anda klik Simpan.",
                "6. Kalau ada 1 baris error, tidak ada yang disimpan. Perbaiki file lalu upload ulang."
            };
            for (int i = 0; i < lines.Length; i++)
                help.Cell(i + 1, 1).Value = lines[i];
            help.Cell(1, 1).Style.Font.Bold = true;
            help.Column(1).Width = 110;

            ws.Columns().AdjustToContents(1, Math.Min(lastRow, 300));
            ws.Column(7).Width = Math.Max(ws.Column(7).Width, 35);
            ws.SheetView.FreezeRows(1);
            ws.SetTabActive();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>Baca & cek file. Tidak ada yang disimpan di sini.</summary>
        public static StockImportResult Read(Stream file, string fileName, IReadOnlyList<Medicine> medicines, IReadOnlyCollection<string> activeSatuan)
        {
            var result = new StockImportResult { FileName = fileName };
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
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Stok Obat", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);
                if (!ws.Cell(1, 1).GetString().Trim().Equals("Nama Obat", StringComparison.OrdinalIgnoreCase)
                    || !ws.Cell(1, 5).GetString().Trim().StartsWith("Jumlah", StringComparison.OrdinalIgnoreCase))
                {
                    result.FileErrors.Add("Format kolom tidak sesuai. Gunakan template dari tombol \"Unduh Template\" (kolom A = Nama Obat, E = Jumlah).");
                    return result;
                }

                var byName = medicines.GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                if (lastRow - 1 > MaxRows)
                {
                    result.FileErrors.Add($"Maksimal {MaxRows} baris sekali import.");
                    return result;
                }

                for (int r = 2; r <= lastRow; r++)
                {
                    var nama = ws.Cell(r, 1).GetString().Trim();
                    var satuan = ws.Cell(r, 2).GetString().Trim();
                    var jenisRaw = ws.Cell(r, 4).GetString().Trim().ToUpperInvariant();
                    var (jumlah, jumlahErr) = ReadInt(ws.Cell(r, 5));
                    var (stokMin, stokMinErr) = ReadInt(ws.Cell(r, 6));
                    var ket = ws.Cell(r, 7).GetString().Trim();

                    if (nama.Length == 0 && satuan.Length == 0 && jenisRaw.Length == 0 && jumlah is null && stokMin is null && ket.Length == 0 && jumlahErr is null)
                        continue;   // baris kosong

                    var row = new StockImportRow
                    {
                        RowNumber = r,
                        Nama = nama,
                        Satuan = satuan.Length == 0 ? null : satuan,
                        Jenis = jenisRaw.Contains(Opname) ? Opname : jenisRaw.Length == 0 || jenisRaw.Contains(Masuk) ? Masuk : jenisRaw,
                        Jumlah = jumlah,
                        StokMinimum = stokMin,
                        Keterangan = ket.Length == 0 ? null : (ket.Length > 200 ? ket[..200] : ket)
                    };
                    result.Rows.Add(row);

                    string? Fail(string msg) { row.Status = StockImportStatus.Error; row.Pesan = msg; return null; }

                    if (nama.Length == 0) { Fail("Nama obat kosong"); continue; }
                    if (nama.Length > 150) { Fail("Nama obat maksimal 150 karakter"); continue; }
                    if (seen.TryGetValue(nama, out var firstRow)) { Fail($"Obat yang sama sudah ada di baris {firstRow}"); continue; }
                    seen[nama] = r;
                    if (jumlahErr is not null) { Fail("Jumlah: " + jumlahErr); continue; }
                    if (stokMinErr is not null) { Fail("Stok minimum: " + stokMinErr); continue; }
                    if (row.Jenis is not (Masuk or Opname)) { Fail($"Jenis \"{jenisRaw}\" tidak dikenal (isi MASUK atau OPNAME)"); continue; }

                    if (byName.TryGetValue(nama, out var med))
                    {
                        row.Nama = med.Name;
                        row.MedicineId = med.Id;
                        row.StokLama = med.Stok;
                        row.StokBaru = med.Stok;
                        row.UbahStokMinimum = stokMin.HasValue && stokMin.Value != med.StokMinimum;

                        if (jumlah is null)
                        {
                            row.Status = StockImportStatus.TanpaPerubahan;
                            row.Pesan = row.UbahStokMinimum ? $"Stok minimum {med.StokMinimum} → {stokMin}" : "Jumlah kosong, dilewati";
                        }
                        else if (row.Jenis == Masuk)
                        {
                            if (jumlah == 0) { Fail("Jumlah stok masuk harus lebih dari 0"); continue; }
                            row.Status = StockImportStatus.Masuk;
                            row.StokBaru = med.Stok + jumlah.Value;
                        }
                        else
                        {
                            row.Status = StockImportStatus.Opname;
                            row.StokBaru = jumlah.Value;
                            if (row.Selisih == 0)
                                row.Pesan = "Stok fisik sama dengan sistem";
                        }

                        if (!med.IsActive)
                            row.Pesan = string.Join(" · ", new[] { row.Pesan, "obat berstatus nonaktif" }.Where(s => !string.IsNullOrEmpty(s)));
                        if (row.Satuan is not null && !row.Satuan.Equals(med.Satuan, StringComparison.OrdinalIgnoreCase))
                            row.Pesan = string.Join(" · ", new[] { row.Pesan, $"satuan di file ({row.Satuan}) diabaikan, tetap {med.Satuan}" }.Where(s => !string.IsNullOrEmpty(s)));
                        row.Satuan = med.Satuan;
                    }
                    else
                    {
                        // Obat baru: satuan wajib dari master satuan obat
                        var match = activeSatuan.FirstOrDefault(s => s.Equals(row.Satuan, StringComparison.OrdinalIgnoreCase));
                        if (row.Satuan is null) { Fail("Obat baru: satuan wajib diisi"); continue; }
                        if (match is null) { Fail($"Obat baru: satuan \"{row.Satuan}\" tidak ada di Master Satuan Obat"); continue; }
                        row.Satuan = match;
                        row.Status = StockImportStatus.ObatBaru;
                        row.StokLama = 0;
                        row.StokBaru = jumlah ?? 0;
                        row.Pesan = "Obat belum ada di sistem, akan ditambahkan";
                    }
                }
            }

            if (result.Rows.Count == 0 && result.FileErrors.Count == 0)
                result.FileErrors.Add("Tidak ada data di file.");
            return result;
        }

        private static (int? Value, string? Error) ReadInt(IXLCell cell)
        {
            if (cell.IsEmpty())
                return (null, null);
            double d;
            if (cell.Value.IsNumber)
                d = cell.Value.GetNumber();
            else
            {
                // Angka yang diketik sebagai teks: jangan tebak pemisah ribuan (1.500 vs 1,5), minta bilangan bulat polos
                var s = cell.GetString().Trim().Replace(" ", "");
                if (s.Length == 0)
                    return (null, null);
                if (!double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                    return (null, $"\"{cell.GetString().Trim()}\" bukan angka");
                if (s.Contains('.') || s.Contains(','))
                    return (null, $"\"{cell.GetString().Trim()}\": tulis bilangan bulat tanpa titik/koma (mis. 1500)");
            }
            if (d < 0) return (null, "tidak boleh negatif");
            if (d != Math.Floor(d)) return (null, "harus bilangan bulat");
            if (d > 1_000_000) return (null, "terlalu besar");
            return ((int)d, null);
        }
    }
}
