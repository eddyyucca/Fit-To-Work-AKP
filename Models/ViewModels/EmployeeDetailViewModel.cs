namespace FitToWorkApp.Models.ViewModels
{
    /// <summary>Halaman Admin/Employees/Detail/{id}.</summary>
    public class EmployeeDetailViewModel
    {
        public Employee Employee { get; set; } = new();
        public EmployeeActivitySummary Summary { get; set; } = new();

        // Bagian yang tampil mengikuti hak akses admin yang membuka
        public bool CanSeeFtw { get; set; }
        public bool CanSeeGym { get; set; }
        public bool CanSeeMedical { get; set; }

        public List<FitToWorkSubmission> Ftw { get; set; } = new();
        public List<GymAttendance> Gym { get; set; } = new();
    }
}
