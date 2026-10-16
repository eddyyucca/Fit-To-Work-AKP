using FitToWorkApp.Services;

namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Satu baris Excel hasil baca (belum disimpan). Disimpan di cache sampai admin klik Simpan.</summary>
    public class EmployeeImportRow
    {
        public int RowNumber { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? NomorHp { get; set; }

        // Teks asli (sudah dirapikan) untuk master yang bisa ditambah/diperbaiki saat pratinjau
        public string? DivisionText { get; set; }
        public string? DepartmentText { get; set; }
        public string? PositionText { get; set; }

        // Id kalau teks cocok persis dengan master aktif; null kalau kosong / perlu keputusan
        public int? DivisionId { get; set; }
        public int? DepartmentId { get; set; }
        public int? PositionId { get; set; }

        public int SectionId { get; set; }
        public string? SectionName { get; set; }

        public DateTime? TanggalLahir { get; set; }
        public string? JenisKelamin { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Alasan baris tidak bisa disimpan. Null = baris valid.</summary>
        public string? Error { get; set; }

        /// <summary>NIP belum ada di sistem saat pratinjau (dicek ulang saat simpan).</summary>
        public bool IsNew { get; set; }

        public bool IsValid => Error is null;

        public string? TextOf(OrgKind kind) => kind switch
        {
            OrgKind.Division => DivisionText,
            OrgKind.Department => DepartmentText,
            _ => PositionText
        };

        public int? IdOf(OrgKind kind) => kind switch
        {
            OrgKind.Division => DivisionId,
            OrgKind.Department => DepartmentId,
            _ => PositionId
        };
    }

    /// <summary>Hasil baca file import (pratinjau) + nama organisasi yang perlu keputusan admin.</summary>
    public class EmployeeImportPreview
    {
        public string Token { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public List<EmployeeImportRow> Rows { get; set; } = new();

        /// <summary>Nama Division / Department / Jabatan yang tidak dikenali (hanya dari baris valid).</summary>
        public List<OrgUnknown> Unknowns { get; set; } = new();

        /// <summary>Data master (aktif & nonaktif) untuk dropdown "Pakai yang ada" dan validasi pilihan.</summary>
        public Dictionary<OrgKind, List<OrgItem>> Masters { get; set; } = new();

        public int TotalRows => Rows.Count;
        public int TotalValid => Rows.Count(r => r.IsValid);
        public int TotalNew => Rows.Count(r => r.IsValid && r.IsNew);
        public int TotalUpdate => Rows.Count(r => r.IsValid && !r.IsNew);
        public int TotalFailed => Rows.Count(r => !r.IsValid);

        public OrgUnknown? UnknownFor(OrgKind kind, string? text)
        {
            var key = EmployeeImportRules.Key(text);
            return key.Length == 0 ? null : Unknowns.FirstOrDefault(u => u.Kind == kind && u.Key == key);
        }
    }

    /// <summary>Data siap simpan (sudah termasuk keputusan admin) untuk EmployeeImportRepository.</summary>
    public class EmployeeImportCommit
    {
        public List<EmployeeImportRow> Rows { get; set; } = new();

        /// <summary>Keputusan per nama tidak dikenali, kunci = (Kind, Key).</summary>
        public Dictionary<(OrgKind Kind, string Key), (OrgUnknown Unknown, OrgChoice Choice)> Decisions { get; set; } = new();
    }
}
