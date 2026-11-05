using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Contracts;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;
using Ashen.Promotions.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Ordering.Features;

/// <summary>The shopping cart, for guests and customers (FR-018). Adding to a cart holds no stock.</summary>
internal static class CartEndpoints
{
    public sealed record CartLineDto(
        Guid VariantId,
        Guid ProductId,
        string ProductName,
        string ProductSlug,
        string? ImageUrl,
        string Sku,
        IReadOnlyDictionary<string, string> OptionValues,
        Money UnitPrice,
        int Quantity,
        Money LineTotal,
        int Available,
        bool IsPurchasable,
        bool PriceChanged);

    /// <param name="CouponProblem">Why the applied coupon no longer works (e.g. the cart dropped below its minimum); checkout lists it too.</param>
    public sealed record CartDto(List<CartLineDto> Items, Money? Subtotal, int ItemCount, bool CanCheckout, string? CouponCode, Money? Discount, string? CouponProblem);
    public sealed record QuantityRequest(int Quantity);

    internal sealed record Ctx(OrderingDbContext Db, CartResolver Carts, ICatalogModule Catalog, IPromotionsModule Promotions, ICurrentUser User, TimeProvider Clock);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var cart = endpoints.MapGroup("/cart").AllowAnonymous().WithTags("Cart");
        cart.MapGet("", Get).Produces<CartDto>();
        cart.MapPut("/items/{variantId:guid}", SetQuantity).Produces<CartDto>();
        CartCoupon.Map(cart);
    }

    private static async Task<IResult> Get([AsParameters] Ctx c, CancellationToken ct) =>
        TypedResults.Ok(await ToDto(await c.Carts.Find(ct), c, ct));

    private static async Task<IResult> SetQuantity(Guid variantId, QuantityRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        Money price = default;
        if (request.Quantity > 0)
        {
            // FR-004: unknown, unpublished or sold-out items can't go in the cart, nor more than is in stock.
            var variant = (await c.Catalog.GetVariantsForCheckout([variantId], ct)).SingleOrDefault();
            if (variant is null || !variant.IsPurchasable)
            {
                return Error.Conflict("cart.unavailable", "This item isn't available right now.").ToProblem();
            }

            if (request.Quantity > variant.Available)
            {
                return Error.Conflict("cart.insufficient_stock", $"Only {variant.Available} of this item are left.").ToProblem();
            }

            price = variant.Price;
        }

        var cart = request.Quantity > 0 ? await c.Carts.GetOrCreate(ct) : await c.Carts.Find(ct);
        if (cart is not null)
        {
            var set = cart.SetQuantity(variantId, request.Quantity, price, c.Clock.GetUtcNow());
            if (!set.IsSuccess)
            {
                return set.Error!.ToProblem();
            }

            await c.Db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await ToDto(cart, c, ct));
    }

    /// <summary>Lines at today's prices, flagged when an item is gone, short of stock or changed price.</summary>
    internal static async Task<CartDto> ToDto(Cart? cart, Ctx c, CancellationToken ct)
    {
        if (cart is null || cart.Items.Count == 0)
        {
            return new CartDto([], null, 0, false, cart?.CouponCode, null, null);
        }

        var variants = await c.Catalog.GetVariantsForCheckout(cart.Items.Select(i => i.VariantId).ToList(), ct);

        // A product deleted outright can't be shown or bought: drop its lines.
        var gone = cart.Items.Where(i => variants.All(v => v.VariantId != i.VariantId)).Select(i => i.VariantId).ToList();
        foreach (var variantId in gone)
        {
            cart.SetQuantity(variantId, 0, default, c.Clock.GetUtcNow());
        }

        if (gone.Count > 0)
        {
            await c.Db.SaveChangesAsync(ct);
        }

        var lines = cart.Items.Select(item =>
        {
            var v = variants.Single(x => x.VariantId == item.VariantId);
            return new CartLineDto(
                v.VariantId, v.ProductId, v.ProductName, v.ProductSlug, v.ImageUrl, v.Sku, v.OptionValues,
                v.Price, item.Quantity, (v.Price * item.Quantity).RoundToCents(), v.Available, v.IsPurchasable,
                PriceChanged: v.Price != item.PriceWhenAdded);
        }).ToList();

        var subtotal = lines.Count == 0 ? (Money?)null : lines.Skip(1).Aggregate(lines[0].LineTotal, (sum, l) => sum + l.LineTotal);
        var canCheckout = lines.Count > 0 && lines.All(l => l.IsPurchasable && l.Quantity <= l.Available);

        Money? discount = null;
        string? couponProblem = null;
        if (cart.CouponCode is { } code && subtotal is { } amount)
        {
            var coupon = await c.Promotions.Validate(code, c.User.UserId, amount, ct);
            discount = coupon.IsValid ? coupon.Discount : null;
            couponProblem = coupon.Refusal;
        }

        return new CartDto(lines, subtotal, lines.Sum(l => l.Quantity), canCheckout && couponProblem is null, cart.CouponCode, discount, couponProblem);
    }
}
