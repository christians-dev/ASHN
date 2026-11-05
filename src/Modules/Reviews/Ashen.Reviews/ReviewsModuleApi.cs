using Ashen.Reviews.Contracts;
using Ashen.Reviews.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Reviews;

internal sealed class ReviewsModuleApi(ReviewsDbContext db) : IReviewsModule
{
    public async Task<IReadOnlyDictionary<Guid, RatingSummary>> GetRatings(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var rows = await db.Reviews.AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId) && !r.IsHidden)
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Average = g.Average(r => (decimal)r.Rating), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProductId, r => new RatingSummary(Math.Round(r.Average, 1, MidpointRounding.AwayFromZero), r.Count));
    }
}
