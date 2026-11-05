using System.Globalization;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Promotions.Domain;

internal enum DiscountType
{
    Percentage,
    FixedAmount,
}

/// <summary>
/// What a coupon gives and when it applies (FR-037). <paramref name="Value"/> is a percentage (0-100] or an amount in
/// <paramref name="Currency"/>, as is <paramref name="MinimumSpend"/>.
/// </summary>
internal sealed record CouponTerms(
    string Code,
    string Currency,
    DiscountType DiscountType,
    decimal Value,
    decimal? MinimumSpend,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? TotalUsageLimit,
    bool OnePerCustomer);

/// <summary>
/// A discount code. Its <see cref="RedemptionCount"/> counts pending and confirmed uses and is changed under the
/// row's concurrency token, so a usage limit holds even when many checkouts race for the last use (FR-038).
/// </summary>
internal sealed class Coupon : AggregateRoot
{
    private Coupon() { }

    public static Coupon Create(CouponTerms terms) => new Coupon { IsActive = true }.Apply(terms);

    public string Code { get; private set; } = "";
    public DiscountType DiscountType { get; private set; }
    public string Currency { get; private set; } = "";
    public decimal Value { get; private set; }
    public decimal? MinimumSpendAmount { get; private set; }
    public Money? MinimumSpend => MinimumSpendAmount is { } amount ? new Money(amount, Currency) : null;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public int? TotalUsageLimit { get; private set; }
    public bool OnePerCustomer { get; private set; }
    public bool IsActive { get; private set; }
    public int RedemptionCount { get; private set; }

    /// <summary>Codes are matched ignoring case and surrounding spaces.</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public void Update(CouponTerms terms) => Apply(terms);

    public void Deactivate() => IsActive = false;

    /// <summary>The discount on <paramref name="subtotal"/>, never more than the subtotal itself.</summary>
    public Money DiscountOn(Money subtotal)
    {
        var discount = DiscountType == DiscountType.Percentage
            ? (subtotal * (Value / 100m)).RoundToCents()
            : new Money(Value, Currency);
        return Money.Min(discount, subtotal);
    }

    /// <summary>The discount, or why the coupon can't be used. <paramref name="alreadyUsedByCustomer"/> matters only for one-per-customer coupons.</summary>
    public Result<Money> Evaluate(Money subtotal, DateTimeOffset now, bool alreadyUsedByCustomer)
    {
        if (!IsActive)
        {
            return Refuse("This code isn't valid.");
        }

        if (now < StartsAt)
        {
            return Refuse("This code isn't active yet.");
        }

        if (EndsAt is { } ends && now >= ends)
        {
            return Refuse($"This code expired on {ends.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}.");
        }

        if (TotalUsageLimit is { } limit && RedemptionCount >= limit)
        {
            return Refuse("This code has been fully used.");
        }

        if (OnePerCustomer && alreadyUsedByCustomer)
        {
            return Refuse("You've already used this code.");
        }

        if (MinimumSpend is { } minimum && subtotal < minimum)
        {
            return Refuse($"Spend at least {Show(minimum)} to use this code.");
        }

        return DiscountOn(subtotal);
    }

    /// <summary>Counts one more use. The caller has evaluated the coupon and saves under the concurrency token.</summary>
    public void Redeem() => RedemptionCount++;

    /// <summary>A pending use was given back (failed or abandoned checkout).</summary>
    public void Unredeem() => RedemptionCount = Math.Max(0, RedemptionCount - 1);

    private Coupon Apply(CouponTerms terms)
    {
        Code = Normalize(terms.Code);
        Currency = terms.Currency;
        DiscountType = terms.DiscountType;
        Value = terms.Value;
        MinimumSpendAmount = terms.MinimumSpend;
        StartsAt = terms.StartsAt;
        EndsAt = terms.EndsAt;
        TotalUsageLimit = terms.TotalUsageLimit;
        OnePerCustomer = terms.OnePerCustomer;
        return this;
    }

    private static Error Refuse(string reason) =>
        Error.Validation("coupon.refused", reason, new Dictionary<string, string[]> { ["code"] = [reason] });

    private static string Show(Money money) => money.Currency == "USD"
        ? "$" + money.Amount.ToString("0.00", CultureInfo.InvariantCulture)
        : money.ToString();
}

internal enum RedemptionStatus
{
    Pending,
    Confirmed,
    Released,
}

/// <summary>One checkout's use of a coupon: Pending while the order is being placed, then Confirmed or Released.</summary>
internal sealed class CouponRedemption : Entity
{
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private CouponRedemption() { }

    public CouponRedemption(Guid couponId, Guid customerId, Guid checkoutId, Money discount, DateTimeOffset now)
    {
        CouponId = couponId;
        CustomerId = customerId;
        CheckoutId = checkoutId;
        Discount = discount;
        Status = RedemptionStatus.Pending;
        ExpiresAt = now + PendingLifetime;
    }

    public Guid CouponId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CheckoutId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Money Discount { get; private set; }
    public RedemptionStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public void Confirm(Guid orderId)
    {
        OrderId = orderId;
        Status = RedemptionStatus.Confirmed;
    }

    public void Release() => Status = RedemptionStatus.Released;
}
