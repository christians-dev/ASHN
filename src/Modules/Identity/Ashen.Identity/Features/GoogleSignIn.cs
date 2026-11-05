using System.Security.Claims;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Identity.Domain;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ashen.Identity.Features;

/// <summary>
/// "Continue with Google" (FR-007, FR-008). A Google login links to an existing account only when Google says the
/// email is verified; an unverified email is refused, so nobody can claim someone else's address.
/// </summary>
internal static class GoogleSignIn
{
    public const string Provider = "Google";
    public const string EmailVerifiedClaim = "email_verified";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/google", Challenge).AllowAnonymous().WithTags("Auth");
        endpoints.MapGet("/auth/google/callback", Callback).AllowAnonymous().WithTags("Auth");
    }

    private static async Task<IResult> Challenge(
        string? returnUrl, IAuthenticationSchemeProvider schemes, SignInManager<Account> signIn, ILoggerFactory loggers)
    {
        returnUrl ??= "/";
        if (!IdentityProblems.IsLocalUrl(returnUrl))
        {
            return Error.Validation("auth.return_url", "Return address must be a page on this site.").ToProblem();
        }

        if (await schemes.GetSchemeAsync(Provider) is null)
        {
            loggers.CreateLogger(typeof(GoogleSignIn).FullName!).LogWarning("Google sign-in requested but Authentication:Google is not configured");
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "Google sign-in isn't available right now.");
        }

        var properties = signIn.ConfigureExternalAuthenticationProperties(
            Provider, $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return TypedResults.Challenge(properties, [Provider]);
    }

    private static async Task<IResult> Callback(
        string? returnUrl,
        HttpContext http,
        UserManager<Account> users,
        SignInManager<Account> signIn,
        InitialAdminSeeder initialAdmin,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        var target = IdentityProblems.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return TypedResults.LocalRedirect("/sign-in?error=external");
        }

        try
        {
            var existingLogin = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (existingLogin.Succeeded)
            {
                return TypedResults.LocalRedirect(target);
            }

            if (existingLogin.IsLockedOut)
            {
                return TypedResults.LocalRedirect("/sign-in?error=locked");
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var verified = bool.TryParse(info.Principal.FindFirstValue(EmailVerifiedClaim), out var v) && v;
            if (email is null || !verified)
            {
                return TypedResults.LocalRedirect("/sign-in?error=unverified");
            }

            var account = await users.FindByEmailAsync(email);
            if (account is null)
            {
                account = new Account
                {
                    Email = email,
                    UserName = email,
                    EmailConfirmed = true,
                    DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0],
                    CreatedAt = clock.GetUtcNow(),
                };
                if (!(await users.CreateAsync(account)).Succeeded)
                {
                    return TypedResults.LocalRedirect("/sign-in?error=external");
                }

                await users.AddToRoleAsync(account, Roles.Customer);
                await initialAdmin.EnsureAdmin(account);
                loggers.CreateLogger(typeof(GoogleSignIn).FullName!).LogInformation("Account {AccountId} registered with Google", account.Id);
            }
            else if (account.Status != AccountStatus.Active)
            {
                return TypedResults.LocalRedirect("/sign-in?error=external");
            }
            else if (!account.EmailConfirmed)
            {
                // Nobody proved they own this address before Google did, so whoever set the password may not (an
                // attacker pre-registering someone's email). The address's owner takes the account over: drop the
                // password and end every other session. A password reset gets one back.
                await users.RemovePasswordAsync(account);
                account.EmailConfirmed = true;
                await users.UpdateAsync(account);
                await users.UpdateSecurityStampAsync(account);
            }

            if (!(await users.AddLoginAsync(account, info)).Succeeded)
            {
                return TypedResults.LocalRedirect("/sign-in?error=external");
            }

            await signIn.SignInAsync(account, isPersistent: true, info.LoginProvider);
            return TypedResults.LocalRedirect(target);
        }
        finally
        {
            await http.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }
}
