using Ashen.BuildingBlocks;

namespace Ashen.UnitTests.BuildingBlocks;

public class MoneyTests
{
    private static Money Usd(decimal amount) => new(amount, "USD");

    [Fact]
    public void Adds_and_subtracts_in_the_same_currency()
    {
        Assert.Equal(Usd(15.75m), Usd(10.50m) + Usd(5.25m));
        Assert.Equal(Usd(5.25m), Usd(10.50m) - Usd(5.25m));
    }

    [Fact]
    public void Multiplies_by_quantity_and_rate()
    {
        Assert.Equal(Usd(31.50m), Usd(10.50m) * 3);
        Assert.Equal(6.25m, (Usd(100m) * 0.0625m).Amount);
    }

    [Theory]
    [InlineData(0.005, 0.01)]
    [InlineData(0.004, 0.00)]
    [InlineData(6.255, 6.26)]
    [InlineData(-0.005, -0.01)]
    public void Rounds_half_away_from_zero_to_cents(decimal amount, decimal expected)
    {
        Assert.Equal(expected, Usd(amount).RoundToCents().Amount);
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        var eur = new Money(1m, "EUR");

        Assert.Throws<InvalidOperationException>(() => Usd(1m) + eur);
        Assert.Throws<InvalidOperationException>(() => Usd(1m) < eur);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("")]
    public void Rejects_invalid_currency_codes(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Compares_and_takes_minimum()
    {
        Assert.True(Usd(1m) < Usd(2m));
        Assert.True(Usd(2m) >= Usd(2m));
        Assert.Equal(Usd(15m), Money.Min(Usd(20m), Usd(15m)));
    }

    [Fact]
    public void Knows_zero_and_negative()
    {
        Assert.True(Money.Zero("USD").IsZero);
        Assert.True(Usd(-0.01m).IsNegative);
        Assert.False(Usd(0m).IsNegative);
    }
}
