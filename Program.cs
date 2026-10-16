using System.Globalization;
using System.Security.Claims;
using FitToWorkApp.Data;
using FitToWorkApp.Hubs;
using FitToWorkApp.Repositories;
using FitToWorkApp.Security;
using FitToWorkApp.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

// NOTE: file ini direkonstruksi dari build yang berjalan (decompile) - komentar aslinya hilang.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IAdminUserRepository, AdminUserRepository>();
builder.Services.AddScoped<IFitToWorkRepository, FitToWorkRepository>();
builder.Services.AddScoped<IFormOptionRepository, FormOptionRepository>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IDashboardExtraRepository, DashboardExtraRepository>();
builder.Services.AddScoped<IGymRepository, GymRepository>();
builder.Services.AddScoped<IActivityTypeRepository, ActivityTypeRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IMedicalMasterRepository, MedicalMasterRepository>();
builder.Services.AddScoped<IMedicalLookupRepository, MedicalLookupRepository>();
builder.Services.AddScoped<IDataResetRepository, DataResetRepository>();
builder.Services.AddScoped<IHealthCheckupRepository, HealthCheckupRepository>();
builder.Services.AddScoped<IClinicVisitRepository, ClinicVisitRepository>();
builder.Services.AddScoped<IDrugAlcoholTestRepository, DrugAlcoholTestRepository>();
builder.Services.AddScoped<IHealthReferenceRepository, HealthReferenceRepository>();
builder.Services.AddScoped<ICheckupParameterRepository, CheckupParameterRepository>();
builder.Services.AddScoped<IMitraRepository, MitraRepository>();
builder.Services.AddScoped<ISiteLocationRepository, SiteLocationRepository>();
builder.Services.AddScoped<IFtwSettingRepository, FtwSettingRepository>();
builder.Services.AddScoped<IEmployeeImportRepository, EmployeeImportRepository>();
builder.Services.AddScoped<IEmployeeDeviceLogin, EmployeeDeviceLogin>();
builder.Services.AddHttpClient(HrisEmployeeSync.HttpClientName);
builder.Services.AddScoped<IHrisEmployeeSync, HrisEmployeeSync>();
builder.Services.AddSingleton<HrisSyncStatus>();
builder.Services.AddHostedService<HrisAutoSyncService>();

var keysPath = builder.Configuration["App:DataProtectionKeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("FitToWorkApp")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddScoped<ILocationGuard, LocationGuard>();
builder.Services.AddHostedService<GymAutoCheckoutService>();
builder.Services.AddSingleton<IMedicalFileStorage, MedicalFileStorage>();
builder.Services.AddScoped<IMedicalAttachmentRepository, MedicalAttachmentRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAccessLogRepository, AccessLogRepository>();
builder.Services.AddScoped<IAccessLogger, AccessLogger>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessLogAuthorizationResultHandler>();
builder.Services.AddHostedService<AccessLogCleanupService>();
builder.Services.AddSingleton<IAppClock, AppClock>();
builder.Services.AddScoped<IDashboardNotifier, DashboardNotifier>();
builder.Services.AddSignalR();

builder.Services.AddAuthentication("Cookies").AddCookie(options =>
{
    options.LoginPath = "/Account/EmployeeLogin";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "FitToWork.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/Portal"))
            ctx.RedirectUri = "/Portal/Login";
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnValidatePrincipal = async ctx =>
    {
        if (ctx.Principal?.IsInRole("Employee") ?? false)
        {
            var deviceLogin = ctx.HttpContext.RequestServices.GetRequiredService<IEmployeeDeviceLogin>();
            var allowed = int.TryParse(ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId)
                && await deviceLogin.IsAllowedAsync(employeeId);
            if (!allowed)
            {
                ctx.RejectPrincipal();
                deviceLogin.Forget(ctx.HttpContext);
                await ctx.HttpContext.SignOutAsync("Cookies");
            }
        }
        else if (ctx.Principal?.IsInRole("Admin") == true)
        {
            // Admin yang dinonaktifkan / dihapus langsung keluar walau cookie-nya masih berlaku.
            var permissions = ctx.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            if (!(await permissions.GetAsync(ctx.Principal)).IsActive)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync("Cookies");
            }
        }
    };

    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/Portal"))
            ctx.RedirectUri = "/Portal/Login";
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EmployeeOnly", policy => policy.RequireRole("Employee"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("PortalOnly", policy => policy.RequireRole("Portal"));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self), payment=()";
    headers["Content-Security-Policy"] = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AccessLogMiddleware>();

app.MapControllerRoute(name: "default", pattern: "{controller=Menu}/{action=Index}/{id?}");
app.MapHub<DashboardHub>("/hubs/dashboard");

using (var scope = app.Services.CreateScope())
{
    try
    {
        // Acuan nilai pemkes yang sudah diubah admin (menu Acuan Nilai Pemkes). Kalau tabel belum ada
        // atau database belum siap, aplikasi tetap jalan dengan nilai default.
        await scope.ServiceProvider.GetRequiredService<IHealthReferenceRepository>().ReloadAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Acuan nilai pemkes memakai default. Detail: {ex.Message}");
    }
}

app.Run();
