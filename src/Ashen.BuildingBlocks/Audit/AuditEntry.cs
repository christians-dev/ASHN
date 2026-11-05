using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ashen.BuildingBlocks.Audit;

/// <summary>Who changed what, and when (FR-047). Stored in each module's own schema.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? ActorUserId { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? Details { get; init; }
    public DateTimeOffset At { get; init; }
}

public interface IAuditLog
{
    /// <summary>Adds an audit entry to <paramref name="db"/>; it is saved with the change it describes.</summary>
    void Record(DbContext db, string action, string entityType, string entityId, object? details = null);
}

internal sealed class AuditLog(IHttpContextAccessor httpContextAccessor, TimeProvider clock) : IAuditLog
{
    public void Record(DbContext db, string action, string entityType, string entityId, object? details = null)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            ActorUserId = Guid.TryParse(userId, out var id) ? id : null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details),
            At = clock.GetUtcNow(),
        });
    }
}
