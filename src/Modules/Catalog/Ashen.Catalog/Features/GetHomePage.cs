using Ashen.Catalog.Contracts;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Catalog.Shared;
using Ashen.Reviews.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features;

/// <summary>Featured products in the admin's order, plus the categories (FR-006).</summary>
internal static class GetHomePage
{
    public sealed record HomePageResponse(List<ProductSummary> Featured, List<ListCategories.CategorySummary> Categories);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/home", Handle).AllowAnonymous().WithTags("Catalog");

    private static async Task<Ok<HomePageResponse>> Handle(CatalogDbContext db, IFileStorage storage, IReviewsModule reviews, CancellationToken cancellationToken)
    {
        var featured = await db.Products.AsNoTracking()
            .Where(p => p.IsFeatured && p.Status == ProductStatus.Published)
            .OrderBy(p => p.FeaturedPosition)
            .ToSummaries(storage, reviews, cancellationToken);

        var categories = await ListCategories.Query(db).ToListAsync(cancellationToken);

        return TypedResults.Ok(new HomePageResponse(featured, categories));
    }
}
