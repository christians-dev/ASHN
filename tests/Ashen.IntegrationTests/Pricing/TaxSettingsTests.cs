using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks.Audit;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;
using Ashen.Ordering.Data;
using Ashen.Pricing.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Pricing;

public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record QuoteDto(MoneyDto Subtotal, MoneyDto Shipping, decimal TaxRate, MoneyDto Tax, MoneyDto Total);

public class TaxSettingsTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> Admin() => await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");

    private static async Task<QuoteDto> Quote(HttpClient client, Guid addressId) =>
        await (await client.PostAsJsonAsync("/api/checkout/quote", new { addressId }, Ct)).ReadAs<QuoteDto>();

    [Fact]
    public async Task Illinois_is_taxed_and_texas_is_not()
    {
        var variant = await factory.CreateProduct("Hundred Dollar Coat", 100m, stock: 5);
        var (client, _, illinois) = await Shopper.New(factory);
        var texas = await client.AddAddress("TX", "73301");
        await client.SetQuantity(variant, 1);

        var il = await Quote(client, illinois);
        var tx = await Quote(client, texas);

        Assert.Equal((0.0625m, 6.25m), (il.TaxRate, il.Tax.Amount));
        Assert.Equal((0m, 0m), (tx.TaxRate, tx.Tax.Amount));
    }

    [Fact]
    public async Task Shipping_fee_change_shows_in_the_next_quote()
    {
        var variant = await factory.CreateProduct("Shipping Fee Tee", 10m, stock: 5);
        var (client, _, address) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/admin/store-settings", new { flatShippingFee = 7.5m }, Ct)).StatusCode);
        var quote = await Quote(client, address);
        await admin.PutAsJsonAsync("/api/admin/store-settings", new { flatShippingFee = 5m }, Ct); // back to the seed value

        Assert.Equal(7.5m, quote.Shipping.Amount);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/store-settings", new { flatShippingFee = -1m }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Rates_are_validated()
    {
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/tax-rates/ZZ", new { rate = 0.05m }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/tax-rates/TX", new { rate = 0.25m }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/tax-rates/TX", new { rate = -0.01m }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Changes_are_audited()
    {
        var admin = await Admin();
        var future = DateTimeOffset.UtcNow.AddYears(5); // scheduled, so it can't affect other tests' quotes
        await admin.PutAsJsonAsync("/api/admin/tax-rates/WA", new { rate = 0.065m, effectiveFrom = future }, Ct);
        await admin.PutAsJsonAsync("/api/admin/store-settings", new { flatShippingFee = 5m }, Ct);

        var actions = await factory.Query(sp => sp.GetRequiredService<PricingDbContext>().Set<AuditEntry>().Select(a => a.Action).ToListAsync());

        Assert.Contains("tax_rate.set", actions);
        Assert.Contains("store_settings.shipping_fee_set", actions);
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/tax-rates" },
        { "PUT", "/api/admin/tax-rates/IL" },
        { "GET", "/api/admin/store-settings" },
        { "PUT", "/api/admin/store-settings" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Pricing_admin_routes_refuse_customers_and_anonymous_visitors(string method, string url)
    {
        var customer = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Customer");
        var anonymous = factory.CreateApiClient();
        await anonymous.RefreshXsrfToken();
        HttpRequestMessage Request() => new(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { rate = 0.01m, flatShippingFee = 1m }) };

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.SendAsync(Request(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(), Ct)).StatusCode);
    }
}

/// <summary>Changes the Illinois rate, so it gets a database of its own.</summary>
public class TaxRateChangeTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record RateRow(string JurisdictionCode, decimal Rate, bool IsCurrent);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_rate_applies_to_new_quotes_and_old_orders_keep_theirs()
    {
        var variant = await factory.CreateProduct("Rate Change Coat", 100m, stock: 5);
        var (client, customerId, address) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(address, Shopper.GoodCard)).StatusCode); // at 6.25%

        var admin = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/admin/tax-rates/IL", new { rate = 0.07m }, Ct)).StatusCode);

        await client.SetQuantity(variant, 1);
        var quote = await (await client.PostAsJsonAsync("/api/checkout/quote", new { addressId = address }, Ct)).ReadAs<QuoteDto>();
        Assert.Equal(7.00m, quote.Tax.Amount);

        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.SingleAsync(o => o.CustomerId == customerId));
        Assert.Equal((0.0625m, 6.25m), (order.TaxRate, order.Tax.Amount));

        var rows = await admin.GetFromJsonAsync<List<RateRow>>("/api/admin/tax-rates", Ct);
        Assert.Equal(0.07m, rows!.Single(r => r.JurisdictionCode == "IL" && r.IsCurrent).Rate);
        Assert.Contains(rows!, r => r.JurisdictionCode == "IL" && r.Rate == 0.0625m && !r.IsCurrent); // history kept
    }
}
