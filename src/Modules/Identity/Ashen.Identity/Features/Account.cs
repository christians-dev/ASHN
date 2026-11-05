using Ashen.BuildingBlocks.Events;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Identity.Domain;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ashen.Identity.Features;

/// <summary>The signed-in customer's own name and email, and deleting the account (FR-015).</summary>
internal static class AccountEndpoints
{
    public sealed record AccountResponse(string Email, string DisplayName);
    public sealed record UpdateAccountRequest(string Email, string DisplayName);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account", Get).WithTags("Account");
        endpoints.MapPut("/account", Update).WithTags("Account");
        endpoints.MapDelete("/account", Delete).WithTags("Account");
    }

    private static async Task<Results<Ok<AccountResponse>, UnauthorizedHttpResult>> Get(HttpContext http, UserManager<Account> users) =>
        await users.GetUserAsync(http.User) is { } account
            ? TypedResults.Ok(new AccountResponse(account.Email!, account.DisplayName))
            : TypedResults.Unauthorized();

    private static async Task<IResult> Update(
        UpdateAccountRequest request, HttpContext http, UserManager<Account> users, SignInManager<Account> signIn)
    {
        if (await users.GetUserAsync(http.User) is not { } account)
        {
            return TypedResults.Unauthorized();
        }

        if (Register.DisplayNameError(request.DisplayName) is { } nameError)
        {
            return nameError.ToProblem();
        }

        var email = request.Email.Trim();
        if (!string.Equals(email, account.Email, StringComparison.OrdinalIgnoreCase))
        {
            // Email doubles as the user name, so both change together.
            account.Email = email;
            account.UserName = email;
            account.EmailConfirmed = false; // not proven until a reset link or Google confirms it
        }

        account.DisplayName = request.DisplayName.Trim();
        var updated = await users.UpdateAsync(account);
        if (!updated.Succeeded)
        {
            return updated.ToValidationError().ToProblem();
        }

        await signIn.RefreshSignInAsync(account); // new name and email in the cookie
        return TypedResults.NoContent();
    }

    /// <summary>Anonymises the account instead of deleting it, so orders keep a valid customer id (spec edge cases).</summary>
    private static async Task<IResult> Delete(
        HttpContext http, UserManager<Account> users, SignInManager<Account> signIn, AdminRoles adminRoles, IEventBus events, ILoggerFactory loggers)
    {
        if (await users.GetUserAsync(http.User) is not { } account)
        {
            return TypedResults.Unauthorized();
        }

        // An admin gives up the role with the account, unless they are the last one (FR-014).
        if (await adminRoles.IsAdmin(account.Id, http.RequestAborted)
            && await adminRoles.Revoke(account.Id, http.RequestAborted) is { IsSuccess: false } refused)
        {
            return refused.Error!.ToProblem();
        }

        foreach (var login in await users.GetLoginsAsync(account))
        {
            await users.RemoveLoginAsync(account, login.LoginProvider, login.ProviderKey);
        }

        await users.RemovePasswordAsync(account);

        var anonymous = $"deleted-{account.Id:N}@deleted.invalid";
        account.Email = anonymous;
        account.UserName = anonymous;
        account.DisplayName = "Former customer";
        account.Status = AccountStatus.Deleted;
        await users.UpdateAsync(account);
        await users.UpdateSecurityStampAsync(account); // ends sessions on other devices

        await signIn.SignOutAsync();
        await events.Publish(new AccountDeletionRequested(account.Id), http.RequestAborted);

        loggers.CreateLogger(typeof(AccountEndpoints).FullName!).LogInformation("Account {AccountId} deleted and anonymised", account.Id);
        return TypedResults.NoContent();
    }
}
