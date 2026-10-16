using System.Security.Claims;
using FitToWorkApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FitToWorkApp.Security
{
    /// <summary>Hak akses efektif seorang admin.</summary>
    public class AdminAccess
    {
        public static readonly AdminAccess None = new(false, null, new HashSet<string>(), isActive: false);

        public AdminAccess(bool isSuperAdmin, string? roleName, IReadOnlySet<string> permissions, bool isActive = true)
        {
            IsActive = isActive;
            IsSuperAdmin = isSuperAdmin;
            RoleName = roleName;
            Permissions = permissions;
        }

        /// <summary>false = akun admin nonaktif / sudah dihapus / bukan admin -> sesi login harus diputus.</summary>
        public bool IsActive { get; }
        public bool IsSuperAdmin { get; }
        public string? RoleName { get; }
        public IReadOnlySet<string> Permissions { get; }

        public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
        public bool HasAny(IEnumerable<string> permissions) => IsSuperAdmin || permissions.Any(Permissions.Contains);
    }

    public interface IPermissionService
    {
        /// <summary>Hak akses user yang sedang login (kosong kalau bukan admin).</summary>
        Task<AdminAccess> GetAsync(ClaimsPrincipal user);
        Task<AdminAccess> GetForAdminAsync(int adminUserId);

        /// <summary>Panggil setiap role/permission/role-user berubah supaya langsung berlaku.</summary>
        void InvalidateAll();
    }

    /// <summary>
    /// Hak akses dibaca dari database (bukan disimpan di cookie), jadi perubahan role langsung berlaku
    /// tanpa perlu admin logout. Hasilnya di-cache singkat supaya tidak query di setiap request.
    /// </summary>
    public class PermissionService : IPermissionService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
        private static int _generation;   // naik setiap InvalidateAll -> semua key cache lama otomatis tidak terpakai

        private readonly IRoleRepository _roleRepository;
        private readonly IMemoryCache _cache;

        public PermissionService(IRoleRepository roleRepository, IMemoryCache cache)
        {
            _roleRepository = roleRepository;
            _cache = cache;
        }

        public Task<AdminAccess> GetAsync(ClaimsPrincipal user)
        {
            if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Admin")
                || !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
                return Task.FromResult(AdminAccess.None);

            return GetForAdminAsync(adminId);
        }

        public async Task<AdminAccess> GetForAdminAsync(int adminUserId)
        {
            var key = $"perm:{Volatile.Read(ref _generation)}:{adminUserId}";
            if (_cache.TryGetValue(key, out AdminAccess? cached) && cached is not null)
                return cached;

            var (isActive, isSuper, roleName, perms) = await _roleRepository.GetAccessForAdminAsync(adminUserId);
            var access = new AdminAccess(isSuper, roleName, perms.ToHashSet(), isActive);
            _cache.Set(key, access, CacheDuration);
            return access;
        }

        public void InvalidateAll() => Interlocked.Increment(ref _generation);
    }

    // ---------------- Integrasi dengan [Authorize] ----------------

    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(IReadOnlyList<string> anyOf) => AnyOf = anyOf;
        public IReadOnlyList<string> AnyOf { get; }
    }

    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissionService;

        public PermissionHandler(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var access = await _permissionService.GetAsync(context.User);
            if (access.HasAny(requirement.AnyOf))
                context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Membuat policy "Perm:a|b" secara dinamis dari [HasPermission(...)], jadi tidak perlu
    /// mendaftarkan satu policy per permission di Program.cs.
    /// </summary>
    public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
                return await base.GetPolicyAsync(policyName);

            var perms = policyName[HasPermissionAttribute.PolicyPrefix.Length..]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new AuthorizationPolicyBuilder()
                .RequireRole("Admin")
                .AddRequirements(new PermissionRequirement(perms))
                .Build();
        }
    }
}
