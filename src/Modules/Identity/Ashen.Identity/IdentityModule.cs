using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Identity.Data;
using Ashen.Identity.Domain;
using Ashen.Identity.Features;
using Ashen.Identity.Features.Admin;
using Ashen.Identity.Passwords;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Identity;

/// <summary>Accounts, roles and sign-in (cookie-based backend-for-frontend, research R2/R3).</summary>
public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DbContextOptionsBuilderExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException("Connection string 'ashen' is not configured.");

        // Registered by hand: this context inherits Identity's base class, not ModuleDbContext.
        services.AddDbContext<AshenIdentityDbContext>((sp, options) => options
            .UseModuleDefaults(connectionString, AshenIdentityDbContext.Schema)
            .AddInterceptors(sp.GetRequiredService<IntegrationEventDispatcher>()));
        services.AddModuleDatabase<AshenIdentityDbContext>();

        // Keys live in the database so every instance shares them (ECS runs several tasks; containers are replaced).
        // shortcut: keys are stored unencrypted in Postgres; add .ProtectKeysWith… (e.g. AWS KMS) for production.
        services.AddDataProtection()
            .SetApplicationName("Ashen")
            .PersistKeysToDbContext<AshenIdentityDbContext>();

        services.AddIdentityCore<Account>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // The user name is the email address, so allow every character an email can contain.
                options.User.AllowedUserNameCharacters = "";
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AshenIdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<AccountClaimsPrincipalFactory>()
            .AddPasswordValidator<BreachedPasswordValidator>();

        // Password reset links expire after 1 hour (FR-009).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();

        // Google is optional: without a client id/secret (e.g. a fresh clone) the store runs with email sign-in only.
        var google = configuration.GetSection("Authentication:Google");
        if (!string.IsNullOrEmpty(google["ClientId"]) && !string.IsNullOrEmpty(google["ClientSecret"]))
        {
            authentication.AddGoogle(GoogleSignIn.Provider, options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                // Register this in Google Cloud Console as the redirect URI (see quickstart.md).
                options.CallbackPath = "/api/auth/signin-google";
                options.ClaimActions.MapCustomJson(GoogleSignIn.EmailVerifiedClaim, json =>
                    json.TryGetProperty("email_verified", out var verified) || json.TryGetProperty("verified_email", out verified)
                        ? verified.ToString().ToLowerInvariant()
                        : null);
            });
        }
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "ashen_auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
            cookie.SlidingExpiration = true;
            // An API answers 401/403; redirects to a login page only make sense for server-rendered pages.
            cookie.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden),
            };
        });

        // Re-read each signed-in account's roles every minute, so granting or revoking Admin (FR-014) reaches open
        // sessions quickly. The default is 30 minutes.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<AdminRoles>();
        services.AddOptions<InitialAdminOptions>().BindConfiguration(InitialAdminOptions.SectionName);
        services.AddScoped<InitialAdminSeeder>();
        services.AddScoped<IModuleSeeder>(sp => sp.GetRequiredService<InitialAdminSeeder>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        GetSession.Map(endpoints);
        Register.Map(endpoints);
        Login.Map(endpoints);
        Logout.Map(endpoints);
        GoogleSignIn.Map(endpoints);
        PasswordReset.Map(endpoints);
        AccountEndpoints.Map(endpoints);

        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Identity admin");
        ManageAdmins.Map(admin);
    }

    private static Task WriteStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
