using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Contracts;
using Ashen.Customers.Data;
using Ashen.Customers.Domain;
using Ashen.Identity.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Customers.Features;

/// <summary>The signed-in customer's saved products (FR-041). Adding and removing are both safe to repeat.</summary>
internal static class Wishlist
{
    /// <param name="IsAvailable">Published and in stock, so it can go in the cart; false once unpublished or sold out.</param>
    public sealed record WishlistItemDto(
        Guid ProductId, string Name, string? Slug, string? ImageUrl, Money? FromPrice, bool IsAvailable, DateTimeOffset AddedAt);

    internal sealed record Ctx(CustomersDbContext Db, ICurrentUser User, ICatalogModule Catalog, TimeProvider Clock);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var wishlist = endpoints.MapGroup("/me/wishlist").WithTags("Wishlist");
        wishlist.MapGet("", List).Produces<List<WishlistItemDto>>();
        wishlist.MapPut("/{productId:guid}", Add);
        wishlist.MapDelete("/{productId:guid}", Remove);
    }

    private static Task<CustomerProfile?> Load(Ctx c, CancellationToken ct) =>
        c.Db.Profiles.Include(p => p.Wishlist).SingleOrDefaultAsync(p => p.Id == c.User.UserId, ct);

    private static async Task<IResult> List([AsParameters] Ctx c, CancellationToken ct)
    {
        var items = (await Load(c, ct))?.Wishlist.OrderByDescending(i => i.AddedAt).ToList() ?? [];
        var products = (await c.Catalog.GetProductSummaries([.. items.Select(i => i.ProductId)], ct)).ToDictionary(p => p.ProductId);

        return TypedResults.Ok(items.Select(i => products.TryGetValue(i.ProductId, out var p)
                // An unpublished product keeps its name but no link: its page is gone (Story 11 #3).
                ? new WishlistItemDto(i.ProductId, p.Name, p.IsPublished ? p.Slug : null, p.ImageUrl, p.FromPrice, p.IsPublished && p.InStock, i.AddedAt)
                : new WishlistItemDto(i.ProductId, "No longer available", null, null, null, false, i.AddedAt))
            .ToList());
    }

    private static async Task<IResult> Add(Guid productId, [AsParameters] Ctx c, CancellationToken ct)
    {
        var product = (await c.Catalog.GetProductSummaries([productId], ct)).SingleOrDefault();
        if (product is not { IsPublished: true })
        {
            return Error.NotFound("product.not_found", "This product isn't available.").ToProblem();
        }

        var profile = await Load(c, ct);
        if (profile is null)
        {
            profile = new CustomerProfile(c.User.UserId!.Value, c.Clock.GetUtcNow());
            c.Db.Profiles.Add(profile);
        }

        profile.AddToWishlist(productId, c.Clock.GetUtcNow());
        try
        {
            await c.Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A double click raced itself: the unique (profile, product) index kept one entry, which is the goal.
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> Remove(Guid productId, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await Load(c, ct) is { } profile)
        {
            profile.RemoveFromWishlist(productId);
            await c.Db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }
}
