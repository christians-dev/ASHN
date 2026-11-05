using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using static Ashen.Ordering.Features.CartEndpoints;

namespace Ashen.Ordering.Features;

/// <summary>Apply or remove the cart's coupon (FR-038). A new code replaces the old one: one coupon per order.</summary>
internal static class CartCoupon
{
    public sealed record CouponRequest(string Code);

    public static void Map(RouteGroupBuilder cart)
    {
        cart.MapPut("/coupon", Apply).Produces<CartDto>();
        cart.MapDelete("/coupon", Remove).Produces<CartDto>();
    }

    private static async Task<IResult> Apply(CouponRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        var cart = await c.Carts.Find(ct);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Refused("Enter a coupon code.");
        }

        // Validated against today's cart, so the shopper sees the discount (or the reason) straight away.
        var dto = await ToDto(cart, c, ct);
        if (cart is null || dto.Subtotal is not { } subtotal)
        {
            return Refused("Add something to your cart first.");
        }

        var check = await c.Promotions.Validate(request.Code, c.User.UserId, subtotal, ct);
        if (!check.IsValid)
        {
            return Refused(check.Refusal!);
        }

        cart.ApplyCoupon(check.Code, c.Clock.GetUtcNow());
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDto(cart, c, ct));
    }

    private static async Task<IResult> Remove([AsParameters] Ctx c, CancellationToken ct)
    {
        var cart = await c.Carts.Find(ct);
        if (cart?.CouponCode is not null)
        {
            cart.RemoveCoupon(c.Clock.GetUtcNow());
            await c.Db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await ToDto(cart, c, ct));
    }

    private static IResult Refused(string reason) =>
        Error.Validation("coupon.refused", reason, new Dictionary<string, string[]> { ["code"] = [reason] }).ToProblem();
}
