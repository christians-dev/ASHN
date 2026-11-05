using System.Net;
using System.Net.Http.Json;

using Ashen.IntegrationTests.Ordering;
using Ashen.Ordering.Data;
using Ashen.Promotions.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Pricing;

/// <summary>
/// SC-010: real checkouts whose totals must match a hand calculation to the cent.
/// Illinois 6.25% on merchandise, rounded half-up; shipping $5.00 and never taxed; other states untaxed.
/// </summary>
public class TotalsAccuracyTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private static readonly Dictionary<string, string> Zip = new()
    {
        ["IL"] = "60601",
        ["TX"] = "73301",
        ["CA"] = "94105",
        ["NY"] = "10001",
        ["WA"] = "98101",
        ["FL"] = "33101",
        ["OR"] = "97201",
        ["IN"] = "46201",
        ["WI"] = "53703",
        ["MO"] = "63101",
    };

    public static TheoryData<string, decimal, int, decimal, decimal, decimal> Rows => new()
    {
        // state, unit price, quantity, subtotal, tax, total
        { "IL", 100.00m, 1, 100.00m, 6.25m, 111.25m },
        { "IL", 19.99m, 3, 59.97m, 3.75m, 68.72m },    // 3.748125 → 3.75
        { "IL", 0.08m, 1, 0.08m, 0.01m, 5.09m },       // 0.005 → 0.01 (half up)
        { "IL", 0.07m, 1, 0.07m, 0.00m, 5.07m },       // 0.004375 → 0.00
        { "IL", 64.00m, 2, 128.00m, 8.00m, 141.00m },
        { "IL", 33.33m, 3, 99.99m, 6.25m, 111.24m },   // 6.249375 → 6.25
        { "IL", 12.50m, 4, 50.00m, 3.13m, 58.13m },    // 3.125 → 3.13
        { "IL", 210.00m, 1, 210.00m, 13.13m, 228.13m }, // 13.125 → 13.13
        { "IL", 9.99m, 10, 99.90m, 6.24m, 111.14m },   // 6.24375 → 6.24
        { "IL", 1.00m, 7, 7.00m, 0.44m, 12.44m },      // 0.4375 → 0.44
        { "IL", 148.00m, 1, 148.00m, 9.25m, 162.25m },
        { "IL", 72.00m, 3, 216.00m, 13.50m, 234.50m },
        { "TX", 100.00m, 1, 100.00m, 0m, 105.00m },
        { "TX", 19.99m, 3, 59.97m, 0m, 64.97m },
        { "CA", 64.00m, 2, 128.00m, 0m, 133.00m },
        { "NY", 33.33m, 3, 99.99m, 0m, 104.99m },
        { "WA", 0.08m, 1, 0.08m, 0m, 5.08m },
        { "FL", 210.00m, 1, 210.00m, 0m, 215.00m },
        { "OR", 9.99m, 10, 99.90m, 0m, 104.90m },
        { "IN", 12.50m, 4, 50.00m, 0m, 55.00m },
        { "WI", 1.00m, 7, 7.00m, 0m, 12.00m },
        { "MO", 72.00m, 3, 216.00m, 0m, 221.00m },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Order_totals_match_the_hand_calculation(string state, decimal unitPrice, int quantity, decimal subtotal, decimal tax, decimal total)
    {
        var variant = await factory.CreateProduct($"Totals {state} {unitPrice} x{quantity}", unitPrice, stock: quantity);
        var (client, customerId, _) = await Shopper.New(factory);
        var address = await client.AddAddress(state, Zip[state]);
        await client.SetQuantity(variant, quantity);

        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(address, Shopper.GoodCard)).StatusCode);

        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.SingleAsync(o => o.CustomerId == customerId));
        Assert.Equal((subtotal, 5.00m, tax, total), (order.Subtotal.Amount, order.Shipping.Amount, order.Tax.Amount, order.Total.Amount));
    }

    public static TheoryData<string, decimal, int, string, decimal, decimal, decimal, decimal> CouponRows => new()
    {
        // state, unit price, quantity, coupon (P = percent, F = fixed), value, discount, tax, total
        { "IL", 50.00m, 1, "P", 10m, 5.00m, 2.81m, 52.81m },    // tax on 45.00: 2.8125 → 2.81
        { "IL", 19.99m, 3, "P", 15m, 9.00m, 3.19m, 59.16m },    // 59.97 × 15% = 8.9955 → 9.00; tax on 50.97: 3.185625 → 3.19
        { "IL", 12.50m, 4, "P", 12.5m, 6.25m, 2.73m, 51.48m },  // tax on 43.75: 2.734375 → 2.73
        { "IL", 33.33m, 3, "F", 10m, 10.00m, 5.62m, 100.61m },  // tax on 89.99: 5.624375 → 5.62
        { "IL", 15.00m, 1, "F", 20m, 15.00m, 0.00m, 5.00m },    // capped at the subtotal; shipping still due
        { "TX", 100.00m, 1, "P", 25m, 25.00m, 0.00m, 80.00m },
    };

    [Theory]
    [MemberData(nameof(CouponRows))]
    public async Task Discounted_order_totals_match_the_hand_calculation(
        string state, decimal unitPrice, int quantity, string type, decimal value, decimal discount, decimal tax, decimal total)
    {
        var code = $"SC{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        await factory.Query(async sp =>
        {
            var db = sp.GetRequiredService<PromotionsDbContext>();
            db.Coupons.Add(Coupon.Create(new CouponTerms(code, "USD", type == "P" ? DiscountType.Percentage : DiscountType.FixedAmount, value, null,
                DateTimeOffset.UtcNow.AddDays(-1), null, null, false)));
            return await db.SaveChangesAsync();
        });
        var variant = await factory.CreateProduct($"Coupon Totals {state} {unitPrice} x{quantity}", unitPrice, stock: quantity);
        var (client, customerId, _) = await Shopper.New(factory);
        var address = await client.AddAddress(state, Zip[state]);
        await client.SetQuantity(variant, quantity);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/cart/coupon", new { code }, TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(address, Shopper.GoodCard)).StatusCode);

        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.SingleAsync(o => o.CustomerId == customerId));
        Assert.Equal((discount, tax, total), (order.Discount.Amount, order.Tax.Amount, order.Total.Amount));
    }
}
