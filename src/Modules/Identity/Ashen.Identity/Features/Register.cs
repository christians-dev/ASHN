using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Identity.Domain;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ashen.Identity.Features;

/// <summary>Email and password sign-up; the new customer is signed in straight away (FR-007, FR-010).</summary>
internal static class Register
{
    public sealed record RegisterRequest(string Email, string Password, string DisplayName);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/auth/register", Handle)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithTags("Auth");

    private static async Task<IResult> Handle(
        RegisterRequest request,
        UserManager<Account> users,
        SignInManager<Account> signIn,
        InitialAdminSeeder initialAdmin,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        if (DisplayNameError(request.DisplayName) is { } nameError)
        {
            return nameError.ToProblem();
        }

        var account = new Account
        {
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await users.CreateAsync(account, request.Password);
        if (!created.Succeeded)
        {
            return created.ToValidationError().ToProblem();
        }

        await users.AddToRoleAsync(account, Roles.Customer);
        await initialAdmin.EnsureAdmin(account);
        await signIn.SignInAsync(account, isPersistent: true);

        loggers.CreateLogger(typeof(Register).FullName!).LogInformation("Account {AccountId} registered", account.Id);
        return TypedResults.NoContent();
    }

    public static Error? DisplayNameError(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100
            ? Error.Validation("account.display_name", "Please fix the highlighted fields.",
                new Dictionary<string, string[]> { ["displayName"] = ["Enter a name of up to 100 characters."] })
            : null;
}
