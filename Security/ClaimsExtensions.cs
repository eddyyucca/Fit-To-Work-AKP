using System.Security.Claims;

namespace FitToWorkApp.Security
{
    public static class ClaimsExtensions
    {
        public static int? AdminId(this ClaimsPrincipal user) =>
            user.IsInRole("Admin") && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        /// <summary>Nama lengkap admin (dipakai sebagai nama pemeriksa / petugas di data medis & kartu stok).</summary>
        public static string AdminName(this ClaimsPrincipal user) =>
            user.Identity?.Name ?? user.FindFirstValue("Username") ?? "(admin)";
    }
}
