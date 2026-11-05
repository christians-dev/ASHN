using Ashen.BuildingBlocks;
using Ashen.Promotions.Contracts;
using Ashen.Promotions.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Promotions;

internal sealed class PromotionsModuleApi(PromotionsDbContext db, TimeProvider clock) : IPromotionsModule
{
    // Enough for a burst of checkouts racing on one coupon; each retry re-reads the count, so limits still hold.
    private const int MaxAttempts = 10;

    public async Task<CouponCheck> Validate(string code, Guid? customerId, Money subtotal, CancellationToken cancellationToken = default)
    {
        var normalized = Coupon.Normalize(code);
        var coupon = await db.Coupons.AsNoTracking().SingleOrDefaultAsync(c => c.Code == normalized, cancellationToken);
        if (coupon is null)
        {
            return new CouponCheck(false, normalized, Money.Zero(subtotal.Currency), "This code isn't valid.");
        }

        var used = customerId is { } id && await UsedBy(coupon.Id, id, null, cancellationToken);
        var result = coupon.Evaluate(subtotal, clock.GetUtcNow(), used);
        return result.IsSuccess
            ? new CouponCheck(true, coupon.Code, result.Value, null)
            : new CouponCheck(false, coupon.Code, Money.Zero(subtotal.Currency), result.Error!.Message);
    }

    public async Task<Result<Money>> ReserveCoupon(
        string code, Guid customerId, Guid checkoutId, Money subtotal, CancellationToken cancellationToken = default)
    {
        var normalized = Coupon.Normalize(code);
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var earlier = await db.Redemptions.AsNoTracking().SingleOrDefaultAsync(r => r.CheckoutId == checkoutId, cancellationToken);
            if (earlier is { Status: not RedemptionStatus.Released })
            {
                return earlier.Discount;
            }

            if (await db.Coupons.SingleOrDefaultAsync(c => c.Code == normalized, cancellationToken) is not { } coupon)
            {
                return Error.Validation("coupon.refused", "This code isn't valid.");
            }

            var now = clock.GetUtcNow();
            var evaluated = coupon.Evaluate(subtotal, now, await UsedBy(coupon.Id, customerId, checkoutId, cancellationToken));
            if (!evaluated.IsSuccess)
            {
                return evaluated;
            }

            coupon.Redeem();
            if (earlier is not null)
            {
                db.Redemptions.Remove(earlier); // a released hold from an earlier try of this checkout makes way for the new one
            }

            db.Redemptions.Add(new CouponRedemption(coupon.Id, customerId, checkoutId, evaluated.Value, now));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return evaluated.Value;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another checkout changed the coupon's count first: read it again and re-check the limits.
            }
        }
    }

    public async Task ConfirmCoupon(Guid checkoutId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var redemption = await db.Redemptions.SingleOrDefaultAsync(r => r.CheckoutId == checkoutId && r.Status == RedemptionStatus.Pending, cancellationToken);
        if (redemption is not null)
        {
            redemption.Confirm(orderId);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ReleaseCoupon(Guid checkoutId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var redemption = await db.Redemptions.SingleOrDefaultAsync(r => r.CheckoutId == checkoutId && r.Status == RedemptionStatus.Pending, cancellationToken);
            if (redemption is null)
            {
                return;
            }

            redemption.Release();
            (await db.Coupons.SingleAsync(c => c.Id == redemption.CouponId, cancellationToken)).Unredeem();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
            }
        }
    }

    /// <summary>Whether the customer holds or has used this coupon on another checkout.</summary>
    private Task<bool> UsedBy(Guid couponId, Guid customerId, Guid? exceptCheckoutId, CancellationToken cancellationToken) =>
        db.Redemptions.AnyAsync(
            r => r.CouponId == couponId && r.CustomerId == customerId && r.Status != RedemptionStatus.Released && r.CheckoutId != exceptCheckoutId,
            cancellationToken);
}
