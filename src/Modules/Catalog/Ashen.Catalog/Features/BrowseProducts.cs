using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Contracts;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Catalog.Shared;
using Ashen.Reviews.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features;

/// <summary>Published products by category, text, size, colour and price, sorted and paged (FR-001, FR-002).</summary>
internal static class BrowseProducts
{
    public sealed record BrowseQuery(
        [FromQuery(Name = "category")] string? Category,
        [FromQuery(Name = "q")] string? Q,
        [FromQuery(Name = "size")] string? Size,
        [FromQuery(Name = "colour")] string? Colour,
        [FromQuery(Name = "minPrice")] decimal? MinPrice,
        [FromQuery(Name = "maxPrice")] decimal? MaxPrice,
        [FromQuery(Name = "sort")] string? Sort,
        [FromQuery(Name = "page")] int Page = 1,
        [FromQuery(Name = "pageSize")] int PageSize = 24);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/products", Handle).AllowAnonymous().WithTags("Catalog");

    private static async Task<Ok<PagedResult<ProductSummary>>> Handle(
        [AsParameters] BrowseQuery query, CatalogDbContext db, IFileStorage storage, IReviewsModule reviews, CancellationToken cancellationToken)
    {
        var paging = new PagedRequest(query.Page, query.PageSize);
        var products = Filter(db, query);
        var total = await products.CountAsync(cancellationToken);

        var items = query.Sort == "rating"
            ? await ByRating(db, products, paging, storage, reviews, cancellationToken)
            : await Sort(products, query.Sort)
                .Skip(paging.Skip)
                .Take(paging.SafePageSize)
                .ToSummaries(storage, reviews, cancellationToken);

        return TypedResults.Ok(new PagedResult<ProductSummary>(items, paging.SafePage, paging.SafePageSize, total));
    }

    private static IQueryable<Product> Filter(CatalogDbContext db, BrowseQuery query)
    {
        var products = db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            products = products.Where(p => db.Categories.Any(c => c.Id == p.CategoryId && c.Slug == query.Category));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{EscapeLike(query.Q.Trim())}%";
            products = products.Where(p => EF.Functions.ILike(p.Name + " " + p.Description, pattern));
        }

        // "Size" and "Colour" are the standard option names the storefront filters on (spec FR-002).
        var size = query.Size?.Trim().ToLowerInvariant();
        var colour = query.Colour?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(size) || !string.IsNullOrEmpty(colour) || query.MinPrice is not null || query.MaxPrice is not null)
        {
            products = products.Where(p => p.Variants.Any(v =>
                (string.IsNullOrEmpty(size) || v.OptionValues.Any(o => o.Name.ToLower() == "size" && o.Value.ToLower() == size))
                && (string.IsNullOrEmpty(colour) || v.OptionValues.Any(o => o.Name.ToLower() == "colour" && o.Value.ToLower() == colour))
                && (query.MinPrice == null || v.Price.Amount >= query.MinPrice)
                && (query.MaxPrice == null || v.Price.Amount <= query.MaxPrice)));
        }

        return products;
    }

    /// <summary>
    /// Best rated first (then most reviewed, then newest); unrated products last. Ratings live in the Reviews module,
    /// so the matching products are ranked here rather than in SQL.
    /// </summary>
    // shortcut: ranks every matching product in memory, fine for a catalog of hundreds; at thousands, keep a copy of
    // each product's rating in Catalog (updated by a "rating changed" event) and sort in SQL.
    private static async Task<List<ProductSummary>> ByRating(
        CatalogDbContext db, IQueryable<Product> products, PagedRequest paging, IFileStorage storage, IReviewsModule reviews, CancellationToken ct)
    {
        var candidates = await products.Select(p => new { p.Id, p.PublishedAt }).ToListAsync(ct);
        var ratings = await reviews.GetRatings([.. candidates.Select(p => p.Id)], ct);
        var pageIds = candidates
            .OrderByDescending(p => ratings.GetValueOrDefault(p.Id)?.Average ?? -1m)
            .ThenByDescending(p => ratings.GetValueOrDefault(p.Id)?.Count ?? 0)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Id)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .Select(p => p.Id)
            .ToList();

        var summaries = await db.Products.AsNoTracking().Where(p => pageIds.Contains(p.Id)).ToSummaries(storage, reviews, ct);
        return [.. summaries.OrderBy(s => pageIds.IndexOf(s.ProductId))];
    }

    private static IQueryable<Product> Sort(IQueryable<Product> products, string? sort) => sort switch
    {
        "price_asc" => products.OrderBy(p => p.Variants.Min(v => v.Price.Amount)).ThenBy(p => p.Id),
        "price_desc" => products.OrderByDescending(p => p.Variants.Min(v => v.Price.Amount)).ThenBy(p => p.Id),
        _ => products.OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id),
    };

    // Npgsql's ILIKE uses backslash as the escape character.
    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
