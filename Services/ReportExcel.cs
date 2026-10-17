using ClosedXML.Excel;

namespace FitToWorkApp.Services
{
    /// <summary>Pembantu kecil untuk menulis tabel laporan ke Excel (judul, header berwarna, baris data).</summary>
    public static class ReportExcel
    {
        public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public static void SetCell(IXLCell cell, object? v)
        {
            switch (v)
            {
                case null: break;
                case string s: cell.Value = s; break;
                case int i: cell.Value = i; break;
                case long l: cell.Value = l; break;
                case decimal d: cell.Value = d; break;
                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm";
                    break;
                case bool b: cell.Value = b; break;
                default: cell.Value = v.ToString() ?? string.Empty; break;
            }
        }

        /// <summary>Menulis judul + 2 baris keterangan. Mengembalikan nomor baris awal tabel pertama.</summary>
        public static int WriteTitle(IXLWorksheet ws, string title, string subtitle, string generated)
        {
            ws.Cell(1, 1).Value = title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = subtitle;
            ws.Cell(3, 1).Value = generated;
            return 5;
        }

        /// <summary>Menulis satu tabel (judul, header, baris). Mengembalikan nomor baris kosong berikutnya.</summary>
        public static int WriteTable<T>(IXLWorksheet ws, int row, string title, string[] headers, IEnumerable<T> rows, params Func<T, object?>[] cols)
        {
            ws.Cell(row, 1).Value = title;
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            for (var i = 0; i < headers.Length; i++)
                ws.Cell(row, i + 1).Value = headers[i];
            var head = ws.Range(row, 1, row, headers.Length);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            row++;

            var any = false;
            foreach (var item in rows)
            {
                any = true;
                for (var i = 0; i < cols.Length; i++)
                    SetCell(ws.Cell(row, i + 1), cols[i](item));
                row++;
            }
            if (!any)
            {
                ws.Cell(row, 1).Value = "(tidak ada data)";
                ws.Cell(row, 1).Style.Font.Italic = true;
                row++;
            }
            return row + 1;
        }
    }
}
