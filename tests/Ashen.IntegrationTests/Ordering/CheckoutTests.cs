using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Payments.Background;
using Ashen.Payments.Contracts;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ashen.IntegrationTests.Ordering;

/// <summary>Shared catalog for checkout tests: products are created per test so stock never leaks between them.</summary>
public class CheckoutFactory : AshenApiFactory
{
    public async Task<Guid> CreateProduct(string name, decimal price, int stock, bool published = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var category = await db.Categories.FirstOrDefaultAsync() ?? db.Categories.Add(new Category("Test", "test", 0)).Entity;

        var product = Product.Create(name, name.ToLowerInvariant().Replace(' ', '-') + "-" + Guid.NewGuid().ToString("N")[..6], "", category.Id, DateTimeOffset.UtcNow);
        product.DefineOptions([new ProductOption("Size", ["M"])]);
        var variant = product.AddVariant($"SKU-{Guid.NewGuid():N}"[..20], new Dictionary<string, string> { ["Size"] = "M" }, new Money(price, "USD"), stock).Value;
        product.AddImage("seed/test.svg", name);
        if (published)
        {
            product.Publish(DateTimeOffset.UtcNow);
        }

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    internal Task<Variant> VariantOf(Guid id) => Query(sp => sp.GetRequiredService<CatalogDbContext>().Variants.AsNoTracking().SingleAsync(v => v.Id == id));
}

/// <summary>Makes saving any order fail, to prove a failure after payment approval charges nobody (FR-021).</summary>
public sealed class FailingOrderSaveFactory : CheckoutFactory
{
    private sealed class ThrowOnOrderInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Added)
                ? throw new InvalidOperationException("Simulated database failure while saving the order.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.ConfigureDbContext<OrderingDbContext>(o => o.AddInterceptors(new ThrowOnOrderInsert())));
    }
}

public static class Shopper
{
    public static readonly object GoodCard = new { number = "4242 4242 4242 4242", expMonth = 12, expYear = 2030, cvc = "123" };
    public static readonly object DecliningCard = new { number = "4000 0000 0000 0002", expMonth = 12, expYear = 2030, cvc = "123" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Created(Guid Id);
    public sealed record Placed(Guid OrderId, string OrderNumber);

    /// <summary>A signed-in customer with one Illinois address.</summary>
    public static async Task<(HttpClient Client, Guid CustomerId, Guid AddressId)> New(AshenApiFactory factory)
    {
        var id = Guid.CreateVersion7();
        var client = await factory.CreateApiClient().ActAs(id, "Customer");
        var address = await client.PostAsJsonAsync("/api/me/addresses", new
        {
            recipientName = "Sam Lee",
            line1 = "1 Main St",
            line2 = (string?)null,
            city = "Chicago",
            stateCode = "IL",
            postalCode = "60601",
            countryCode = "US",
            phone = "312 555 0100",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, address.StatusCode);
        return (client, id, (await address.ReadAs<Created>()).Id);
    }

    /// <summary>Adds another address, e.g. in a state without sales tax.</summary>
    public static async Task<Guid> AddAddress(this HttpClient client, string state, string zip)
    {
        var address = await client.PostAsJsonAsync("/api/me/addresses", new
        {
            recipientName = "Sam Lee",
            line1 = "1 Main St",
            line2 = (string?)null,
            city = "Somewhere",
            stateCode = state,
            postalCode = zip,
            countryCode = "US",
            phone = "555 0100",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, address.StatusCode);
        return (await address.ReadAs<Created>()).Id;
    }

    public static Task<HttpResponseMessage> SetQuantity(this HttpClient client, Guid variantId, int quantity) =>
        client.PutAsJsonAsync($"/api/cart/items/{variantId}", new { quantity }, Ct);

    public static Task<HttpResponseMessage> Checkout(this HttpClient client, Guid addressId, object card, Guid? key = null) =>
        client.PostAsJsonAsync("/api/checkout", new { addressId, card, idempotencyKey = key ?? Guid.NewGuid() }, Ct);
}

public class CheckoutTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record Money2(decimal Amount, string Currency);
    private sealed record Quote(Money2 Subtotal, Money2 Shipping, Money2 Tax, Money2 Total, List<string> Problems);
    private sealed record CartLine(Guid VariantId, int Quantity, bool PriceChanged);
    private sealed record CartDto(List<CartLine> Items, int ItemCount, bool CanCheckout);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<List<Order>> OrdersOf(Guid customerId) =>
        factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().Include(o => o.Lines).Where(o => o.CustomerId == customerId).ToListAsync());

    private Task<List<Payment>> PaymentsOf(Guid customerId) =>
        factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().Where(p => p.CustomerId == customerId).ToListAsync());

    [Fact]
    public async Task Successful_checkout_takes_stock_charges_and_emails()
    {
        var variant = await factory.CreateProduct("Checkout Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 2);

        var quote = await (await client.PostAsJsonAsync("/api/checkout/quote", new { addressId }, Ct)).ReadAs<Quote>();
        Assert.Equal((40m, 5m, 2.50m, 47.50m), (quote.Subtotal.Amount, quote.Shipping.Amount, quote.Tax.Amount, quote.Total.Amount)); // IL 6.25%

        var response = await client.Checkout(addressId, Shopper.GoodCard);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var placed = await response.ReadAs<Shopper.Placed>();
        Assert.Matches(@"^ASH-\d{6}$", placed.OrderNumber);

        var order = Assert.Single(await OrdersOf(customerId));
        Assert.Equal((OrderStatus.Paid, 47.50m, "4242"), (order.Status, order.Total.Amount, order.CardLast4));
        Assert.Equal(PaymentStatus.Captured, Assert.Single(await PaymentsOf(customerId)).Status);

        var stock = await factory.VariantOf(variant);
        Assert.Equal((3, 0), (stock.StockOnHand, stock.StockReserved));
        Assert.True(await factory.Query(sp => sp.GetRequiredService<CatalogDbContext>().Products.AnyAsync(p => p.HasBeenOrdered && p.Variants.Any(v => v.Id == variant))));

        Assert.Contains(placed.OrderNumber, Assert.Single(factory.Emails.To(TestAuthHandler.EmailFor(customerId))).Subject, StringComparison.Ordinal);
        Assert.Equal(0, (await client.GetFromJsonAsync<CartDto>("/api/cart", Ct))!.ItemCount);
    }

    [Fact]
    public async Task Declined_card_creates_no_order_and_puts_stock_back()
    {
        var variant = await factory.CreateProduct("Decline Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);

        var response = await client.Checkout(addressId, Shopper.DecliningCard);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await OrdersOf(customerId));
        Assert.Equal(PaymentStatus.Declined, Assert.Single(await PaymentsOf(customerId)).Status);
        var stock = await factory.VariantOf(variant);
        Assert.Equal((5, 0), (stock.StockOnHand, stock.StockReserved));
        Assert.Equal(1, (await client.GetFromJsonAsync<CartDto>("/api/cart", Ct))!.ItemCount); // the cart is kept
    }

    [Fact]
    public async Task Cart_refuses_unknown_unpublished_sold_out_and_excess_quantities()
    {
        var inStock = await factory.CreateProduct("Five Left", 10m, stock: 5);
        var soldOut = await factory.CreateProduct("None Left", 10m, stock: 0);
        var unpublished = await factory.CreateProduct("Not Yet", 10m, stock: 5, published: false);
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();

        Assert.Equal(HttpStatusCode.Conflict, (await client.SetQuantity(Guid.NewGuid(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SetQuantity(soldOut, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SetQuantity(unpublished, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SetQuantity(inStock, 6)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SetQuantity(inStock, 5)).StatusCode);
    }

    [Fact]
    public async Task Guest_cart_merges_into_the_customer_cart_after_sign_in()
    {
        var variant = await factory.CreateProduct("Guest Tee", 10m, stock: 9);
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        var asGuest = await client.SetQuantity(variant, 2);
        Assert.Contains(asGuest.Headers.GetValues("Set-Cookie"), c => c.StartsWith("ashen_cart=", StringComparison.Ordinal));

        await client.ActAs(Guid.CreateVersion7(), "Customer"); // same browser, now signed in
        var response = await client.GetAsync("/api/cart", Ct);

        Assert.Equal(2, (await response.ReadAs<CartDto>()).ItemCount);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("ashen_cart=;", StringComparison.Ordinal)); // deleted
    }

    [Fact]
    public async Task Same_idempotency_key_places_one_order()
    {
        var variant = await factory.CreateProduct("Double Click Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        var key = Guid.NewGuid();

        var first = await (await client.Checkout(addressId, Shopper.GoodCard, key)).ReadAs<Shopper.Placed>();
        var second = await client.Checkout(addressId, Shopper.GoodCard, key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.OrderNumber, (await second.ReadAs<Shopper.Placed>()).OrderNumber);
        Assert.Single(await OrdersOf(customerId));
        Assert.Equal(4, (await factory.VariantOf(variant)).StockOnHand);
    }

    [Fact]
    public async Task Order_keeps_the_price_paid_after_the_price_changes()
    {
        var variant = await factory.CreateProduct("Price Change Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        await client.Checkout(addressId, Shopper.GoodCard);

        await factory.Query(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Variants.Any(v => v.Id == variant));
            product.UpdateVariant(variant, new Dictionary<string, string> { ["Size"] = "M" }, new Money(99m, "USD"));
            return await db.SaveChangesAsync();
        });

        Assert.Equal(20m, Assert.Single(Assert.Single(await OrdersOf(customerId)).Lines).UnitPrice.Amount);
    }

    [Fact]
    public async Task Item_unpublished_after_adding_blocks_checkout()
    {
        var variant = await factory.CreateProduct("Vanishing Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        await factory.Query(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            (await db.Products.SingleAsync(p => p.Variants.Any(v => v.Id == variant))).Unpublish();
            return await db.SaveChangesAsync();
        });

        var cart = await client.GetFromJsonAsync<CartDto>("/api/cart", Ct);
        var response = await client.Checkout(addressId, Shopper.GoodCard);

        Assert.False(cart!.CanCheckout);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await OrdersOf(customerId));
        Assert.Empty(await PaymentsOf(customerId)); // stopped before any payment
    }

    [Fact]
    public async Task Stale_authorization_without_an_order_is_voided()
    {
        var customerId = Guid.CreateVersion7();
        var paymentId = await factory.Query(async sp => (await sp.GetRequiredService<IPaymentsModule>().Authorize(
            Guid.NewGuid(), customerId, new Money(10m, "USD"), new NewCardSource("4242424242424242", 12, 2030, "123"))).PaymentId!.Value);

        var voider = new StaleAuthorizationVoider(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(DateTimeOffset.UtcNow + StaleAuthorizationVoider.MaxAge + TimeSpan.FromMinutes(1)),
            NullLogger<StaleAuthorizationVoider>.Instance);

        Assert.True(await voider.VoidStale(Ct) >= 1);
        Assert.Equal(PaymentStatus.Voided, (await PaymentsOf(customerId)).Single(p => p.Id == paymentId).Status);
    }
}

public class CheckoutSaveFailureTests(FailingOrderSaveFactory factory) : IClassFixture<FailingOrderSaveFactory>
{
    [Fact]
    public async Task Failure_saving_the_order_voids_the_payment_and_releases_stock()
    {
        var variant = await factory.CreateProduct("Unlucky Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 2);

        var response = await client.Checkout(addressId, Shopper.GoodCard);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var payment = Assert.Single(await factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().Where(p => p.CustomerId == customerId).ToListAsync()));
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        var stock = await factory.VariantOf(variant);
        Assert.Equal((5, 0), (stock.StockOnHand, stock.StockReserved));
        Assert.Empty(await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.Where(o => o.CustomerId == customerId).ToListAsync()));
    }
}
