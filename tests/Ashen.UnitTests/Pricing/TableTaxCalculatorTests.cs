using Ashen.BuildingBlocks;
using Ashen.Pricing.Domain;
using Ashen.Pricing.Tax;

namespace Ashen.UnitTests.Pricing;

public class TableTaxCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Region = Guid.CreateVersion7();
    private static Money Usd(decimal amount) => new(amount, "USD");

    private static readonly TaxRate[] Rates =
    [
        new(Region, "IL", 0.0625m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
    ];

    [Fact]
    public void Illinois_is_6_25_percent()
    {
        var tax = TableTaxCalculator.Calculate(Usd(100m), "IL", Rates, Now);

        Assert.Equal(Usd(6.25m), tax.Tax);
        Assert.Equal(0.0625m, tax.Rate);
        Assert.Equal("IL", tax.Jurisdiction);
    }

    [Fact]
    public void State_without_a_rate_pays_no_tax()
    {
        Assert.Equal(Usd(0m), TableTaxCalculator.Calculate(Usd(100m), "TX", Rates, Now).Tax);
    }

    [Theory]
    [InlineData(10.00, 0.63)]  // 0.625 rounds half up
    [InlineData(0.08, 0.01)]   // 0.005 rounds half up
    [InlineData(0.07, 0.00)]   // 0.004375 rounds down
    public void Rounds_half_up_to_the_cent(decimal amount, decimal expected)
    {
        Assert.Equal(Usd(expected), TableTaxCalculator.Calculate(Usd(amount), "IL", Rates, Now).Tax);
    }

    [Fact]
    public void Latest_rate_already_in_effect_wins()
    {
        TaxRate[] rates =
        [
            new(Region, "IL", 0.0625m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new(Region, "IL", 0.07m, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            new(Region, "IL", 0.09m, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)), // not yet
        ];

        Assert.Equal(Usd(7m), TableTaxCalculator.Calculate(Usd(100m), "IL", rates, Now).Tax);
    }
}
