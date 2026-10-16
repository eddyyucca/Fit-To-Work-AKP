namespace FitToWorkApp.Models.ViewModels
{
    public class ImportEmployeeRowResult
    {
        public int RowNumber { get; set; }
        public string NIP { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsNew { get; set; }
    }

    public class ImportEmployeeResultViewModel
    {
        public List<ImportEmployeeRowResult> Rows { get; set; } = new();

        /// <summary>Perubahan data organisasi dari keputusan di pratinjau, mis. "Jabatan baru: Welder".</summary>
        public List<string> OrgNotes { get; set; } = new();

        public int TotalRows => Rows.Count;
        public int TotalSuccess => Rows.Count(r => r.IsSuccess);
        public int TotalCreated => Rows.Count(r => r.IsSuccess && r.IsNew);
        public int TotalUpdated => Rows.Count(r => r.IsSuccess && !r.IsNew);
        public int TotalFailed => Rows.Count(r => !r.IsSuccess);
    }
}
