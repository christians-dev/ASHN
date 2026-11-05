using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features.Admin;

/// <summary>Replaces the home page's featured products with this ordered list (FR-006, FR-042).</summary>
internal static class SetFeaturedProducts
{
    public sealed record FeaturedRequest(List<Guid> ProductIds);

    public static void Map(RouteGroupBuilder admin) => admin.MapPut("/featured", Handle);

    private static async Task<IResult> Handle(FeaturedRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        var ids = request.ProductIds.Distinct().ToList();
        var products = await c.Db.Products.Where(p => p.IsFeatured || ids.Contains(p.Id)).ToListAsync(ct);

        var notShowable = ids.Where(id => products.SingleOrDefault(p => p.Id == id)?.Status != ProductStatus.Published).ToList();
        if (notShowable.Count > 0)
        {
            return Error.Validation("featured.not_published", "Only published products can be featured.").ToProblem();
        }

        foreach (var product in products)
        {
            var position = ids.IndexOf(product.Id);
            product.SetFeatured(position < 0 ? null : position + 1);
        }

        c.Audit.Record(c.Db, "featured.set", "FeaturedProducts", "home", ids);
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }
}
