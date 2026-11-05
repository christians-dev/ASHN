using System.Data;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.Identity.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Identity.Shared;

/// <summary>
/// Removes the Admin role while making sure one admin always remains (FR-014). Used by revoking and by account
/// deletion. The admin role rows are locked for the check-and-delete, so two admins revoking each other at the same
/// moment can't both succeed and leave the store with none.
/// </summary>
internal sealed class AdminRoles(AshenIdentityDbContext db, IAuditLog audit)
{
    public static readonly Error LastAdmin =
        Error.Conflict("admin.last", "You're the last admin. Make someone else an admin first.");

    /// <summary>Revokes the role. NotFound if the account isn't an admin; Conflict if it's the last one.</summary>
    public async Task<Result> Revoke(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var admins = await db.Database
            .SqlQuery<Guid>($"SELECT user_id AS \"Value\" FROM identity.user_roles WHERE role_id = {AshenIdentityDbContext.AdminRoleId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        if (!admins.Contains(userId))
        {
            return Error.NotFound("admin.not_found", "That account isn't an admin.");
        }

        if (admins.Count == 1)
        {
            return LastAdmin;
        }

        await db.UserRoles.Where(r => r.UserId == userId && r.RoleId == AshenIdentityDbContext.AdminRoleId).ExecuteDeleteAsync(cancellationToken);
        audit.Record(db, "admin.revoked", "Account", userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public Task<bool> IsAdmin(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles.AnyAsync(r => r.UserId == userId && r.RoleId == AshenIdentityDbContext.AdminRoleId, cancellationToken);
}
