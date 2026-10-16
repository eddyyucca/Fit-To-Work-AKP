using FitToWorkApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FitToWorkApp.Hubs
{
    /// <summary>
    /// Hub SignalR untuk Dashboard (Fit To Work, Klinik, Gym). Satu arah: server -> browser.
    /// Saat connect, koneksi dimasukkan ke grup sesuai hak akses admin, jadi event klinik
    /// hanya sampai ke role yang boleh melihat data klinik, dst.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(Permissions.FtwDashboard, Permissions.MedicalVisit, Permissions.GymHistory)]
    public class DashboardHub : Hub
    {
        public const string Path = "/hubs/dashboard";

        // Grup
        public const string GroupFtw = "dash-ftw";
        public const string GroupClinic = "dash-clinic";
        public const string GroupGym = "dash-gym";
        public static readonly string[] AllGroups = { GroupFtw, GroupClinic, GroupGym };

        // Event
        public const string RegisteredEvent = "FtwRegistered";   // ada submit Fit To Work (dengan detail untuk toast)
        public const string GymEvent = "GymChanged";             // ada check-in gym
        public const string ClinicEvent = "ClinicChanged";       // kunjungan klinik baru / dibatalkan
        public const string RefreshEvent = "DashboardRefresh";   // data master berubah (karyawan, reset data) -> muat ulang semua

        private readonly IPermissionService _permissionService;

        public DashboardHub(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        public override async Task OnConnectedAsync()
        {
            var access = await _permissionService.GetAsync(Context.User!);
            if (access.Has(Permissions.FtwDashboard))
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupFtw);
            if (access.Has(Permissions.MedicalVisit))
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupClinic);
            if (access.Has(Permissions.GymHistory))
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupGym);

            await base.OnConnectedAsync();
        }
    }
}
