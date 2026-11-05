using Ashen.BuildingBlocks;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Reviews.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features;

/// <summary>One published product with its images, options and variants; 404 otherwise (Story 1 #4).</summary>
internal static class GetProduct
{
    public sealed record ProductDetail(
        Guid Id,
        string Name,
        string Slug,
        string Description,
        string CategoryName,
        string CategorySlug,
        List<ImageDto> Images,
        List<OptionDto> Options,
        List<VariantDto> Variants,
        RatingSummary? Rating);

    public sealed record ImageDto(string Url, string AltText);
    public sealed record OptionDto(string Name, List<string> Values);
    public sealed record VariantDto(Guid Id, string Sku, Dictionary<string, string> OptionValues, Money Price, bool IsAvailable);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/products/{slug}", Handle).AllowAnonymous().WithTags("Catalog");

    private static async Task<Results<Ok<ProductDetail>, ProblemHttpResult>> Handle(
        string slug, CatalogDbContext db, IFileStorage storage, IReviewsModule reviews, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Slug == slug && p.Status == ProductStatus.Published, cancellationToken);

        if (product is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "This product isn't available.");
        }

        var rating = (await reviews.GetRatings([product.Id], cancellationToken)).GetValueOrDefault(product.Id);
        var category = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == product.CategoryId, cancellationToken);

        return TypedResults.Ok(new ProductDetail(
            product.Id,
            product.Name,
            product.Slug,
            product.Description,
            category.Name,
            category.Slug,
            product.Images.OrderBy(i => i.SortOrder).Select(i => new ImageDto(storage.PublicUrl(i.StoragePath), i.AltText)).ToList(),
            product.Options.Select(o => new OptionDto(o.Name, o.Values)).ToList(),
            product.Variants.Select(v => new VariantDto(
                v.Id, v.Sku, v.OptionValues.ToDictionary(o => o.Name, o => o.Value), v.Price, v.IsAvailable)).ToList(),
            rating));
    }
}
