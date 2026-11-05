using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ashen.Identity.Features;

/// <summary>Email and password sign-in, locked for 15 minutes after 5 failures (FR-011).</summary>
internal static class Login
{
    public sealed record LoginRequest(string Email, string Password);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/auth/login", Handle)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithTags("Auth");

    private static async Task<IResult> Handle(
        LoginRequest request, UserManager<Account> users, SignInManager<Account> signIn, ILoggerFactory loggers)
    {
        // Same answer for "no such account" and "wrong password", so emails can't be probed.
        var invalid = Error.Validation("auth.invalid_credentials", "Email or password is incorrect.").ToProblem();

        var account = await users.FindByEmailAsync(request.Email.Trim());
        if (account is null || account.Status != AccountStatus.Active)
        {
            return invalid;
        }

        var result = await signIn.PasswordSignInAsync(account, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            loggers.CreateLogger(typeof(Login).FullName!).LogWarning("Sign-in locked out for account {AccountId}", account.Id);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status423Locked,
                type: "auth.locked_out",
                detail: "Too many failed attempts. Please try again in 15 minutes.");
        }

        return result.Succeeded ? TypedResults.NoContent() : invalid;
    }
}

internal static class Logout
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/auth/logout", async (SignInManager<Account> signIn) =>
            {
                await signIn.SignOutAsync();
                return TypedResults.NoContent();
            })
            .WithTags("Auth");
}
