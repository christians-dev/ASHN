using Ashen.BuildingBlocks;
using Ashen.Catalog.Contracts;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Reviews.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Shared;

/// <summary>The one query that turns products into list rows, shared by home, browse and the module contract.</summary>
internal static class ProductSummaries
{
    public static async Task<List<ProductSummary>> ToSummaries(
        this IQueryable<Product> products, IFileStorage storage, IReviewsModule reviews, CancellationToken cancellationToken)
    {
        var rows = await products
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Status,
                Image = p.Images.OrderBy(i => i.SortOrder).Select(i => i.StoragePath).FirstOrDefault(),
                FromPrice = p.Variants.Min(v => (decimal?)v.Price.Amount),
                Currency = p.Variants.Select(v => v.Price.Currency).FirstOrDefault(),
                InStock = p.Variants.Any(v => v.StockOnHand - v.StockReserved > 0),
            })
            .ToListAsync(cancellationToken);

        var ratings = await reviews.GetRatings([.. rows.Select(r => r.Id)], cancellationToken);
        return rows.Select(r => new ProductSummary(
                r.Id,
                r.Name,
                r.Slug,
                r.Image is null ? null : storage.PublicUrl(r.Image),
                r.FromPrice is { } amount && r.Currency is { } currency ? new Money(amount, currency) : null,
                r.InStock,
                r.Status == ProductStatus.Published,
                ratings.GetValueOrDefault(r.Id)?.Average,
                ratings.GetValueOrDefault(r.Id)?.Count ?? 0))
            .ToList();
    }
}
