using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Identity.Data;
using Ashen.Identity.Domain;
using Ashen.Identity.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Identity.Features.Admin;

/// <summary>
/// Who runs the store (FR-014): list admins, grant the role to an existing account by email, revoke it. Changes reach
/// the person's open sessions at the next cookie check (within a minute) or when they sign in again.
/// </summary>
internal static class ManageAdmins
{
    public sealed record AdminDto(Guid UserId, string Email, string DisplayName);
    public sealed record GrantRequest(string Email);

    public static void Map(RouteGroupBuilder admin)
    {
        var admins = admin.MapGroup("/admins");
        admins.MapGet("", List).Produces<List<AdminDto>>();
        admins.MapPost("", Grant);
        admins.MapDelete("/{userId:guid}", Revoke);
    }

    private static async Task<IResult> List(UserManager<Account> users) =>
        TypedResults.Ok((await users.GetUsersInRoleAsync(Roles.Admin))
            .OrderBy(a => a.Email)
            .Select(a => new AdminDto(a.Id, a.Email ?? "", a.DisplayName))
            .ToList());

    private static async Task<IResult> Grant(GrantRequest request, UserManager<Account> users, AshenIdentityDbContext db, IAuditLog audit)
    {
        var account = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (account is null || account.Status == AccountStatus.Deleted)
        {
            return Error.NotFound("account.not_found", "No account uses that email. Ask them to register first.").ToProblem();
        }

        if (await users.IsInRoleAsync(account, Roles.Admin))
        {
            return TypedResults.NoContent();
        }

        var added = await users.AddToRoleAsync(account, Roles.Admin);
        if (!added.Succeeded)
        {
            return Error.Conflict("admin.grant_failed", "The admin role couldn't be granted. Try again.").ToProblem();
        }

        audit.Record(db, "admin.granted", nameof(Account), account.Id.ToString());
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Revoke(Guid userId, AdminRoles roles, CancellationToken ct) =>
        (await roles.Revoke(userId, ct)).ToHttp(TypedResults.NoContent);
}
