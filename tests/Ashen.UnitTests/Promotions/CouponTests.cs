using Ashen.BuildingBlocks;
using Ashen.Promotions.Domain;

namespace Ashen.UnitTests.Promotions;

public class CouponTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static Money Usd(decimal amount) => new(amount, "USD");

    private static Coupon Make(
        DiscountType type = DiscountType.Percentage,
        decimal value = 10m,
        decimal? minimum = null,
        DateTimeOffset? starts = null,
        DateTimeOffset? ends = null,
        int? limit = null,
        bool onePerCustomer = false) =>
        Coupon.Create(new CouponTerms(" welcome10 ", "USD", type, value, minimum, starts ?? Now.AddDays(-1), ends, limit, onePerCustomer));

    private static string Reason(Result<Money> result) => Assert.IsType<Error>(result.Error).Message;

    [Fact]
    public void Code_is_normalised()
    {
        Assert.Equal("WELCOME10", Make().Code);
    }

    [Theory]
    [InlineData(50.00, 5.00)]
    [InlineData(19.99, 2.00)] // 1.999 → 2.00
    [InlineData(0.04, 0.00)]  // 0.004 → 0.00
    public void Percentage_is_rounded_to_cents(decimal subtotal, decimal discount)
    {
        Assert.Equal(Usd(discount), Make().Evaluate(Usd(subtotal), Now, false).Value);
    }

    [Fact]
    public void Fixed_amount_is_capped_at_the_subtotal()
    {
        var coupon = Make(DiscountType.FixedAmount, 20m);

        Assert.Equal(Usd(15m), coupon.Evaluate(Usd(15m), Now, false).Value);
        Assert.Equal(Usd(20m), coupon.Evaluate(Usd(60m), Now, false).Value);
    }

    [Fact]
    public void Minimum_spend_refusal_states_the_minimum()
    {
        var coupon = Make(minimum: 75m);

        Assert.Equal("Spend at least $75.00 to use this code.", Reason(coupon.Evaluate(Usd(60m), Now, false)));
        Assert.True(coupon.Evaluate(Usd(75m), Now, false).IsSuccess);
    }

    [Fact]
    public void Only_valid_inside_its_date_window()
    {
        Assert.Contains("isn't active yet", Reason(Make(starts: Now.AddHours(1)).Evaluate(Usd(50m), Now, false)));
        Assert.Contains("expired on Nov 3, 2026", Reason(Make(ends: new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)).Evaluate(Usd(50m), Now, false)));
        Assert.True(Make(ends: Now.AddSeconds(1)).Evaluate(Usd(50m), Now, false).IsSuccess);
    }

    [Fact]
    public void Total_usage_limit_counts_redemptions_and_releases()
    {
        var coupon = Make(limit: 1);
        coupon.Redeem();

        Assert.Contains("fully used", Reason(coupon.Evaluate(Usd(50m), Now, false)));

        coupon.Unredeem();
        Assert.True(coupon.Evaluate(Usd(50m), Now, false).IsSuccess);
    }

    [Fact]
    public void One_per_customer_refuses_a_second_use()
    {
        Assert.Contains("already used", Reason(Make(onePerCustomer: true).Evaluate(Usd(50m), Now, alreadyUsedByCustomer: true)));
        Assert.True(Make().Evaluate(Usd(50m), Now, alreadyUsedByCustomer: true).IsSuccess);
    }

    [Fact]
    public void Inactive_coupon_is_refused()
    {
        var coupon = Make();
        coupon.Deactivate();

        Assert.Contains("isn't valid", Reason(coupon.Evaluate(Usd(50m), Now, false)));
    }
}
