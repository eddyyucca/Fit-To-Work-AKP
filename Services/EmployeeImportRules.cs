using System.Text;

namespace FitToWorkApp.Services
{
    /// <summary>Master organisasi yang boleh "ditambah / diperbaiki" saat import karyawan.</summary>
    public enum OrgKind { Division, Department, Position }

    /// <summary>Keputusan admin untuk satu nama yang tidak dikenali.</summary>
    public enum OrgResolveAction
    {
        /// <summary>Tambahkan sebagai data master baru.</summary>
        Add,
        /// <summary>Pakai data master aktif yang sudah ada (perbaiki typo / beda penulisan).</summary>
        Map,
        /// <summary>Nama sudah ada tapi nonaktif → aktifkan lagi lalu pakai.</summary>
        Reactivate,
        /// <summary>Kosongkan kolom ini untuk baris-baris tersebut.</summary>
        Clear
    }

    public record OrgItem(int Id, string Name, bool IsActive);

    /// <summary>Satu nama di Excel yang tidak cocok dengan master aktif (dikelompokkan, bukan per baris).</summary>
    public class OrgUnknown
    {
        public OrgKind Kind { get; set; }

        /// <summary>Kunci pembanding (huruf besar, spasi dirapikan).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Teks pertama yang ditemukan di Excel (spasi dirapikan) → dipakai sebagai nama kalau "Tambah baru".</summary>
        public string Text { get; set; } = string.Empty;

        public int Count { get; set; }
        public List<int> RowNumbers { get; set; } = new();

        /// <summary>Id data master dengan nama sama yang sedang nonaktif (kalau ada).</summary>
        public int? InactiveId { get; set; }

        /// <summary>Usulan data master aktif yang paling mirip (kalau cukup mirip).</summary>
        public int? SuggestedId { get; set; }
        public string? SuggestedName { get; set; }

        public OrgResolveAction DefaultAction { get; set; }
    }

    public record OrgChoice(OrgResolveAction Action, int? MapToId);

    /// <summary>
    /// Logika murni pencocokan nama Division / Department / Jabatan saat import karyawan
    /// (tanpa database / Excel) supaya bisa di-unit-test.
    /// </summary>
    public static class EmployeeImportRules
    {
        /// <summary>Kemiripan minimal (0–1) supaya sebuah data master diusulkan sebagai perbaikan.</summary>
        public const double MinSuggestSimilarity = 0.7;

        public const int MaxNameLength = 150;

        public static string Label(OrgKind kind) => kind switch
        {
            OrgKind.Division => "Division",
            OrgKind.Department => "Department",
            _ => "Jabatan"
        };

        /// <summary>Rapikan spasi (termasuk spasi ganda / tab). Null kalau kosong.</summary>
        public static string? Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        public static string Key(string? text) => (Clean(text) ?? string.Empty).ToUpperInvariant();

        /// <summary>Kunci tanpa tanda baca / spasi, untuk menilai kemiripan ("Sr. Operator" ≈ "SR OPERATOR").</summary>
        private static string LooseKey(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.ToUpperInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Kemiripan 0–1 berdasarkan jarak Levenshtein (abaikan huruf besar/kecil, spasi & tanda baca).</summary>
        public static double Similarity(string a, string b)
        {
            var x = LooseKey(a);
            var y = LooseKey(b);
            if (x.Length == 0 && y.Length == 0) return 1;
            if (x.Length == 0 || y.Length == 0) return 0;
            return 1.0 - (double)Levenshtein(x, y) / Math.Max(x.Length, y.Length);
        }

        private static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        /// <summary>Id data master AKTIF yang namanya sama persis (abaikan huruf besar/kecil & spasi ganda).</summary>
        public static int? FindActive(IEnumerable<OrgItem> items, string? text)
        {
            var key = Key(text);
            if (key.Length == 0) return null;
            return items.FirstOrDefault(i => i.IsActive && Key(i.Name) == key)?.Id;
        }

        /// <summary>
        /// Kelompokkan nama yang tidak cocok dengan master aktif, lengkap dengan usulan & aksi default:
        /// ada tapi nonaktif → Aktifkan lagi; ada yang mirip → Pakai yang ada; selain itu → Tambah baru.
        /// </summary>
        /// <param name="texts">(nomor baris Excel, teks kolom) — teks kosong diabaikan.</param>
        public static List<OrgUnknown> FindUnknowns(OrgKind kind, IEnumerable<(int Row, string? Text)> texts, IReadOnlyList<OrgItem> items)
        {
            var result = new Dictionary<string, OrgUnknown>();
            foreach (var (row, raw) in texts)
            {
                var clean = Clean(raw);
                if (clean is null || FindActive(items, clean) is not null)
                    continue;

                var key = Key(clean);
                if (!result.TryGetValue(key, out var u))
                {
                    u = new OrgUnknown { Kind = kind, Key = key, Text = clean };
                    u.InactiveId = items.FirstOrDefault(i => !i.IsActive && Key(i.Name) == key)?.Id;

                    var best = items.Where(i => i.IsActive)
                        .Select(i => (Item: i, Score: Similarity(clean, i.Name)))
                        .OrderByDescending(t => t.Score).ThenBy(t => t.Item.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (best.Item is not null && best.Score >= MinSuggestSimilarity)
                    {
                        u.SuggestedId = best.Item.Id;
                        u.SuggestedName = best.Item.Name;
                    }

                    u.DefaultAction = u.InactiveId is not null ? OrgResolveAction.Reactivate
                        : u.SuggestedId is not null ? OrgResolveAction.Map
                        : OrgResolveAction.Add;
                    result[key] = u;
                }
                u.Count++;
                u.RowNumbers.Add(row);
            }

            return result.Values.OrderByDescending(u => u.Count).ThenBy(u => u.Text, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string ActionCode(OrgResolveAction a) => a switch
        {
            OrgResolveAction.Add => "add",
            OrgResolveAction.Map => "map",
            OrgResolveAction.Reactivate => "reactivate",
            _ => "clear"
        };

        /// <summary>
        /// Baca pilihan admin dari form pratinjau. Null + pesan error kalau tidak valid
        /// (mis. "Pakai yang ada" tanpa memilih, id bukan data aktif, "Aktifkan lagi" padahal tidak ada yang nonaktif).
        /// </summary>
        public static OrgChoice? ParseChoice(OrgUnknown u, string? action, int? mapToId, IReadOnlyList<OrgItem> items, out string? error)
        {
            error = null;
            var label = $"{Label(u.Kind)} \"{u.Text}\"";
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "add":
                    if (u.Text.Length > MaxNameLength)
                    {
                        error = $"{label}: nama terlalu panjang (maks {MaxNameLength} karakter).";
                        return null;
                    }
                    return new OrgChoice(OrgResolveAction.Add, null);
                case "map":
                    if (mapToId is null || !items.Any(i => i.IsActive && i.Id == mapToId))
                    {
                        error = $"{label}: pilih {Label(u.Kind).ToLowerInvariant()} pengganti dari daftar.";
                        return null;
                    }
                    return new OrgChoice(OrgResolveAction.Map, mapToId);
                case "reactivate":
                    if (u.InactiveId is null)
                    {
                        error = $"{label}: tidak ada data nonaktif untuk diaktifkan.";
                        return null;
                    }
                    return new OrgChoice(OrgResolveAction.Reactivate, u.InactiveId);
                case "clear":
                    return new OrgChoice(OrgResolveAction.Clear, null);
                default:
                    error = $"{label}: pilih tindakan (tambah / pakai yang ada / kosongkan).";
                    return null;
            }
        }
    }
}
