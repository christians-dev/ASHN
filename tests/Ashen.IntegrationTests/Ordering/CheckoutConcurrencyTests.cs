using System.Net;

using Ashen.IntegrationTests.Infrastructure;
using Ashen.Ordering.Data;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Ordering;

/// <summary>SC-007: 100 shoppers race for the last unit; exactly one gets it and nobody else is charged.</summary>
public class CheckoutConcurrencyTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    [Fact]
    public async Task Hundred_simultaneous_checkouts_for_the_last_unit_create_exactly_one_order()
    {
        var lastUnit = await factory.CreateProduct("Last Unit Cap", 25m, stock: 1);
        var shoppers = new List<(HttpClient Client, Guid AddressId)>();
        for (var i = 0; i < 100; i++)
        {
            var (client, _, addressId) = await Shopper.New(factory);
            Assert.Equal(HttpStatusCode.OK, (await client.SetQuantity(lastUnit, 1)).StatusCode); // carts hold no stock
            shoppers.Add((client, addressId));
        }

        var responses = await Task.WhenAll(shoppers.Select(s => s.Client.Checkout(s.AddressId, Shopper.GoodCard)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(99, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var stock = await factory.VariantOf(lastUnit);
        Assert.Equal((0, 0), (stock.StockOnHand, stock.StockReserved));

        var orders = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.CountAsync(o => o.Lines.Any(l => l.VariantId == lastUnit)));
        var payments = await factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().ToListAsync());
        Assert.Equal(1, orders);
        Assert.Equal(1, payments.Count(p => p.Status == PaymentStatus.Captured));
        Assert.DoesNotContain(payments, p => p.Status == PaymentStatus.Authorized); // nobody left with money on hold
    }
}
