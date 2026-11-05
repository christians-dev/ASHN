using Microsoft.EntityFrameworkCore;

namespace Ashen.BuildingBlocks.Data;

public static class SaveChangesExtensions
{
    /// <summary>Saves, turning a lost optimistic-concurrency race into a 409 instead of a 500 (constitution VIII).</summary>
    public static async Task<Result> TrySaveChangesAsync(this DbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency", "Someone else changed this at the same time. Reload and try again.");
        }
    }
}
