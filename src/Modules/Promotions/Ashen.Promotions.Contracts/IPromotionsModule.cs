using Ashen.BuildingBlocks;

namespace Ashen.Promotions.Contracts;

/// <summary>What Ordering may ask Promotions (module-contracts.md). One coupon per order (FR-038).</summary>
public interface IPromotionsModule
{
    /// <summary>
    /// The discount <paramref name="code"/> gives on <paramref name="subtotal"/>, or why it can't be used. Guests have no
    /// <paramref name="customerId"/>, so the one-per-customer rule is checked again when they reserve it.
    /// </summary>
    Task<CouponCheck> Validate(string code, Guid? customerId, Money subtotal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds one use of the coupon for this checkout and returns the discount. Repeats for the same checkout return the
    /// same hold. Fails with the refusal reason, e.g. when the last use was just taken.
    /// </summary>
    Task<Result<Money>> ReserveCoupon(string code, Guid customerId, Guid checkoutId, Money subtotal, CancellationToken cancellationToken = default);

    /// <summary>The order was saved: the use is final.</summary>
    Task ConfirmCoupon(Guid checkoutId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Checkout failed: the held use is given back. Does nothing if there is no pending hold.</summary>
    Task ReleaseCoupon(Guid checkoutId, CancellationToken cancellationToken = default);
}

/// <param name="Code">The code as stored (upper case).</param>
/// <param name="Refusal">Why it can't be used, for the shopper; null when <paramref name="IsValid"/>.</param>
public sealed record CouponCheck(bool IsValid, string Code, Money Discount, string? Refusal);
