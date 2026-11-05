using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Email;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Domain;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashen.Identity.Features;

/// <summary>Forgot password → emailed single-use link valid for 1 hour → new password (FR-009).</summary>
internal static class PasswordReset
{
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/password/forgot", Forgot).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithTags("Auth");
        endpoints.MapPost("/auth/password/reset", Reset).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithTags("Auth");
    }

    private static async Task<IResult> Forgot(
        ForgotPasswordRequest request,
        UserManager<Account> users,
        IEmailSender email,
        IOptions<BuildingBlocks.StoreOptions> store,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var account = await users.FindByEmailAsync(request.Email.Trim());
        if (account is { Status: AccountStatus.Active, Email: { } address })
        {
            var token = await users.GeneratePasswordResetTokenAsync(account);
            var link = store.Value.Link($"/reset-password?email={Uri.EscapeDataString(address)}&token={Uri.EscapeDataString(token)}");
            try
            {
                await email.Send(new EmailMessage(
                    address,
                    $"Reset your {store.Value.StoreName} password",
                    $"Use this link within 1 hour to choose a new password:\n\n{link}\n\nIf you didn't ask for this, you can ignore this email."),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still 202: an error here must not reveal that the account exists.
                loggers.CreateLogger(typeof(PasswordReset).FullName!).LogError(ex, "Password reset email failed for account {AccountId}", account.Id);
            }
        }

        // Always the same answer, so the endpoint can't be used to find out who has an account.
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> Reset(ResetPasswordRequest request, UserManager<Account> users)
    {
        var account = await users.FindByEmailAsync(request.Email.Trim());
        if (account is null || account.Status != AccountStatus.Active)
        {
            return Error.Validation("auth.invalid_token", "This reset link is invalid or has expired.").ToProblem();
        }

        // Succeeding changes the security stamp, which also makes this link single-use.
        var result = await users.ResetPasswordAsync(account, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.ToValidationError().ToProblem();
        }

        await users.SetLockoutEndDateAsync(account, null);
        await users.ResetAccessFailedCountAsync(account);
        return TypedResults.NoContent();
    }
}
