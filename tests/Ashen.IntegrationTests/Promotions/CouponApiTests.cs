using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;
using Ashen.Ordering.Data;
using Ashen.Promotions.Background;
using Ashen.Promotions.Contracts;
using Ashen.Promotions.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ashen.IntegrationTests.Promotions;

/// <summary>Coupons on the cart and at checkout (FR-037, FR-038), including usage limits under concurrency.</summary>
public class CouponApiTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record MoneyDto(decimal Amount, string Currency);
    private sealed record CartDto(string? CouponCode, MoneyDto? Discount, string? CouponProblem, bool CanCheckout);
    private sealed record QuoteDto(MoneyDto Subtotal, string? CouponCode, MoneyDto Discount, MoneyDto Tax, MoneyDto Total, List<string> Problems);
    private sealed record CouponDto(Guid Id, string Code, int RedemptionCount);
    private sealed record Problem(string? Detail, Dictionary<string, string[]>? Errors);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> Admin() => await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    /// <summary>Creates a coupon through the admin API; returns its code.</summary>
    private async Task<string> CreateCoupon(
        string type = "Percentage", decimal value = 10m, decimal? minimumSpend = null, DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null, int? limit = null, bool onePerCustomer = false)
    {
        var code = NewCode("T");
        var response = await (await Admin()).PostAsJsonAsync("/api/admin/coupons", new
        {
            code,
            discountType = type,
            value,
            minimumSpend,
            startsAt,
            endsAt,
            totalUsageLimit = limit,
            onePerCustomer,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return code;
    }

    /// <summary>A customer with one IL address and a cart worth <paramref name="price"/>.</summary>
    private async Task<(HttpClient Client, Guid CustomerId, Guid AddressId)> ShopperWithCart(decimal price)
    {
        var variant = await factory.CreateProduct($"Coupon Tee {price}", price, stock: 10);
        var shopper = await Shopper.New(factory);
        await shopper.Client.SetQuantity(variant, 1);
        return shopper;
    }

    private static Task<HttpResponseMessage> Apply(HttpClient client, string code) =>
        client.PutAsJsonAsync("/api/cart/coupon", new { code }, Ct);

    private Task<Coupon> CouponNamed(string code) =>
        factory.Query(sp => sp.GetRequiredService<PromotionsDbContext>().Coupons.AsNoTracking().SingleAsync(c => c.Code == code));

    private static async Task<string> Reason(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return Assert.Single((await response.ReadAs<Problem>()).Errors!["code"]);
    }

    [Fact]
    public async Task Apply_replace_and_remove_on_the_cart()
    {
        var (client, _, _) = await ShopperWithCart(50m);
        var tenPercent = await CreateCoupon(value: 10m);
        var twentyOff = await CreateCoupon("FixedAmount", 20m);

        var applied = await (await Apply(client, tenPercent.ToLowerInvariant())).ReadAs<CartDto>();
        Assert.Equal((tenPercent, 5.00m), (applied.CouponCode, applied.Discount!.Amount));

        var replaced = await (await Apply(client, twentyOff)).ReadAs<CartDto>();
        Assert.Equal((twentyOff, 20.00m), (replaced.CouponCode, replaced.Discount!.Amount));

        var removed = await (await client.DeleteAsync("/api/cart/coupon", Ct)).ReadAs<CartDto>();
        Assert.Equal((null, null), (removed.CouponCode, removed.Discount));
    }

    [Fact]
    public async Task Refusals_state_the_reason()
    {
        var (client, _, _) = await ShopperWithCart(60m);
        var expired = await CreateCoupon(startsAt: DateTimeOffset.UtcNow.AddDays(-30), endsAt: DateTimeOffset.UtcNow.AddDays(-1));
        var minimum = await CreateCoupon(minimumSpend: 75m);

        Assert.Contains("expired on", await Reason(await Apply(client, expired)));
        Assert.Equal("Spend at least $75.00 to use this code.", await Reason(await Apply(client, minimum)));
        Assert.Equal("This code isn't valid.", await Reason(await Apply(client, "NOPE-NOT-A-CODE")));
        Assert.Null((await client.GetFromJsonAsync<CartDto>("/api/cart", Ct))!.CouponCode);
    }

    [Fact]
    public async Task Tax_is_charged_after_the_discount()
    {
        var (client, customerId, addressId) = await ShopperWithCart(50m);
        await Apply(client, await CreateCoupon(value: 10m));

        var quote = await (await client.PostAsJsonAsync("/api/checkout/quote", new { addressId }, Ct)).ReadAs<QuoteDto>();
        Assert.Equal((50.00m, 5.00m, 2.81m, 52.81m), (quote.Subtotal.Amount, quote.Discount.Amount, quote.Tax.Amount, quote.Total.Amount)); // 45 × 6.25% = 2.8125

        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(addressId, Shopper.GoodCard)).StatusCode);
        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.CustomerId == customerId));
        Assert.Equal((quote.CouponCode, 5.00m, 2.81m, 52.81m), (order.CouponCode, order.Discount.Amount, order.Tax.Amount, order.Total.Amount));
        Assert.Null((await client.GetFromJsonAsync<CartDto>("/api/cart", Ct))!.CouponCode); // used up with the cart
    }

    [Fact]
    public async Task A_limit_one_coupon_discounts_exactly_one_of_two_parallel_checkouts()
    {
        var code = await CreateCoupon(limit: 1);
        var first = await ShopperWithCart(40m);
        var second = await ShopperWithCart(40m);
        await Apply(first.Client, code);
        await Apply(second.Client, code);

        var responses = await Task.WhenAll(
            first.Client.Checkout(first.AddressId, Shopper.GoodCard),
            second.Client.Checkout(second.AddressId, Shopper.GoodCard));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Contains("fully used", await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync(Ct));
        var discounted = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.CountAsync(o => o.CouponCode == code));
        Assert.Equal(1, discounted);
        Assert.Equal(1, (await CouponNamed(code)).RedemptionCount);
    }

    [Fact]
    public async Task A_declined_payment_gives_the_coupon_use_back()
    {
        var code = await CreateCoupon(limit: 1);
        var (client, _, addressId) = await ShopperWithCart(30m);
        await Apply(client, code);

        Assert.Equal(HttpStatusCode.Conflict, (await client.Checkout(addressId, Shopper.DecliningCard)).StatusCode);
        Assert.Equal(0, (await CouponNamed(code)).RedemptionCount);

        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(addressId, Shopper.GoodCard)).StatusCode);
        Assert.Equal(1, (await CouponNamed(code)).RedemptionCount);
    }

    [Fact]
    public async Task Cancelling_the_order_gives_the_coupon_use_back()
    {
        var code = await CreateCoupon(limit: 1, onePerCustomer: true);
        var (client, _, addressId) = await ShopperWithCart(30m);
        await Apply(client, code);
        var orderId = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;
        Assert.Equal(1, (await CouponNamed(code)).RedemptionCount);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);

        Assert.Equal(0, (await CouponNamed(code)).RedemptionCount);
        await client.SetQuantity(await factory.CreateProduct("Rebuy Coupon Tee", 30m, stock: 5), 1);
        Assert.Equal(HttpStatusCode.OK, (await Apply(client, code)).StatusCode); // the customer can use it again
    }

    [Fact]
    public async Task One_per_customer_coupon_cannot_be_used_twice()
    {
        var code = await CreateCoupon(onePerCustomer: true);
        var (client, _, addressId) = await ShopperWithCart(30m);
        await Apply(client, code);
        Assert.Equal(HttpStatusCode.Created, (await client.Checkout(addressId, Shopper.GoodCard)).StatusCode);

        await client.SetQuantity(await factory.CreateProduct("Second Coupon Tee", 30m, stock: 5), 1);

        Assert.Equal("You've already used this code.", await Reason(await Apply(client, code)));
    }

    [Fact]
    public async Task Abandoned_holds_are_released_after_15_minutes()
    {
        var code = await CreateCoupon(limit: 1);
        await factory.Query(sp => sp.GetRequiredService<IPromotionsModule>()
            .ReserveCoupon(code, Guid.CreateVersion7(), Guid.NewGuid(), new Money(20m, "USD")));
        Assert.Equal(1, (await CouponNamed(code)).RedemptionCount);

        var releaser = new ExpiredRedemptionReleaser(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(DateTimeOffset.UtcNow + CouponRedemption.PendingLifetime + TimeSpan.FromMinutes(1)),
            NullLogger<ExpiredRedemptionReleaser>.Instance);

        Assert.True(await releaser.ReleaseExpired(Ct) >= 1);
        Assert.Equal(0, (await CouponNamed(code)).RedemptionCount);
    }

    [Fact]
    public async Task Admin_edits_deactivates_and_is_audited()
    {
        var admin = await Admin();
        var code = await CreateCoupon(value: 10m);
        var id = (await CouponNamed(code)).Id;

        var edited = await admin.PutAsJsonAsync($"/api/admin/coupons/{id}", new { code, discountType = "FixedAmount", value = 7.5m, onePerCustomer = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/coupons/{id}/deactivate", null, Ct)).StatusCode);

        var (client, _, _) = await ShopperWithCart(20m);
        Assert.Equal("This code isn't valid.", await Reason(await Apply(client, code)));
        var actions = await factory.Query(sp => sp.GetRequiredService<PromotionsDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Equal(["coupon.created", "coupon.deactivated", "coupon.updated"], actions.Order());
    }

    [Theory]
    [InlineData("Percentage", 0, "value")]
    [InlineData("Percentage", 101, "value")]
    [InlineData("FixedAmount", -5, "value")]
    public async Task Invalid_coupons_are_rejected(string type, decimal value, string field)
    {
        var response = await (await Admin()).PostAsJsonAsync("/api/admin/coupons", new { code = NewCode("BAD"), discountType = type, value, onePerCustomer = false }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadAs<Problem>()).Errors!.ContainsKey(field));
    }

    [Fact]
    public async Task Duplicate_codes_are_rejected_ignoring_case()
    {
        var code = await CreateCoupon();

        var response = await (await Admin()).PostAsJsonAsync("/api/admin/coupons", new { code = code.ToLowerInvariant(), discountType = "Percentage", value = 5, onePerCustomer = false }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/coupons" },
        { "POST", "/api/admin/coupons" },
        { "PUT", $"/api/admin/coupons/{Guid.Empty}" },
        { "POST", $"/api/admin/coupons/{Guid.Empty}/deactivate" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Customers_are_forbidden_from_admin_coupon_routes(string method, string path)
    {
        var (client, _, _) = await Shopper.New(factory);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
