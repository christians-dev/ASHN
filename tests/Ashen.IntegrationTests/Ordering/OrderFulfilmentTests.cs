using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks.Audit;
using Ashen.Identity.Contracts;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Ordering;

/// <summary>Order history, customer cancellation and admin fulfilment (FR-035, FR-036).</summary>
public class OrderFulfilmentTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record Summary(Guid Id, string OrderNumber, string Status);
    private sealed record Detail(Guid Id, string OrderNumber, string Status, bool CanCancel, List<Line> Lines);
    private sealed record Line(string ProductName, int Quantity);
    private sealed record Page(List<Summary> Items, int TotalCount);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> Admin() => await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");

    /// <summary>A new customer who has placed one order for <paramref name="quantity"/> of a fresh product (stock 5).</summary>
    private async Task<(HttpClient Client, Guid CustomerId, Guid OrderId, Guid VariantId)> PlacedOrder(int quantity = 2)
    {
        var variant = await factory.CreateProduct("Fulfil Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, quantity);
        var placed = await client.Checkout(addressId, Shopper.GoodCard);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (client, customerId, (await placed.ReadAs<Shopper.Placed>()).OrderId, variant);
    }

    /// <summary>Moves the order's placement time back, as if it had been placed that long ago.</summary>
    private Task Age(Guid orderId, TimeSpan age) =>
        factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PlacedAt, DateTimeOffset.UtcNow - age)));

    private Task<PaymentStatus> PaymentStatusOf(Guid orderId) =>
        factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.Where(p => p.OrderId == orderId).Select(p => p.Status).SingleAsync());

    private async Task AssertCancelledRestockedRefunded(HttpClient client, Guid orderId, Guid variant)
    {
        Assert.Equal("Cancelled", (await client.GetFromJsonAsync<Detail>($"/api/me/orders/{orderId}", Ct))!.Status);
        var stock = await factory.VariantOf(variant);
        Assert.Equal((5, 0), (stock.StockOnHand, stock.StockReserved));
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusOf(orderId));
    }

    [Fact]
    public async Task Customers_see_their_own_orders_newest_first()
    {
        var (client, _, first, _) = await PlacedOrder();
        var addressId = await client.AddAddress("TX", "73301");
        var variant = await factory.CreateProduct("Second Tee", 10m, stock: 5);
        await client.SetQuantity(variant, 1);
        var second = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;

        var orders = await client.GetFromJsonAsync<List<Summary>>("/api/me/orders", Ct);
        var detail = await client.GetFromJsonAsync<Detail>($"/api/me/orders/{first}", Ct);

        Assert.Equal([second, first], orders!.Select(o => o.Id));
        Assert.Equal(("Paid", true), (detail!.Status, detail.CanCancel));
        Assert.Equal(("Fulfil Tee", 2), (detail.Lines.Single().ProductName, detail.Lines.Single().Quantity));
    }

    [Fact]
    public async Task Another_customers_order_is_not_found()
    {
        var (_, _, orderId, _) = await PlacedOrder();
        var (stranger, _, _) = await Shopper.New(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/me/orders/{orderId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<Summary>>("/api/me/orders", Ct))!);
    }

    [Fact]
    public async Task Admin_ships_then_delivers_and_the_customer_is_emailed()
    {
        var (client, customerId, orderId, _) = await PlacedOrder();
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct)).StatusCode);
        var shipped = await client.GetFromJsonAsync<Detail>($"/api/me/orders/{orderId}", Ct);
        Assert.Equal(("Shipped", false), (shipped!.Status, shipped.CanCancel));
        var email = Assert.Single(factory.Emails.To(TestAuthHandler.EmailFor(customerId)), m => m.Subject.Contains("shipped"));
        Assert.Contains($"https://localhost/account/orders/{orderId}", email.TextBody);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/orders/{orderId}/deliver", null, Ct)).StatusCode);
        Assert.Equal("Delivered", (await admin.GetFromJsonAsync<Detail>($"/api/admin/orders/{orderId}", Ct))!.Status);
    }

    [Fact]
    public async Task Invalid_transitions_are_conflicts()
    {
        var (_, _, orderId, _) = await PlacedOrder();
        var admin = await Admin();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/deliver", null, Ct)).StatusCode);
        await admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_cancel_restocks_and_refunds()
    {
        var (client, _, orderId, variant) = await PlacedOrder();
        await Age(orderId, TimeSpan.FromDays(1)); // admins may cancel after the customer's hour

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/api/admin/orders/{orderId}/cancel", null, Ct)).StatusCode);

        await AssertCancelledRestockedRefunded(client, orderId, variant);
    }

    [Fact]
    public async Task Customer_cancels_own_order_within_the_hour()
    {
        var (client, _, orderId, variant) = await PlacedOrder();
        await Age(orderId, TimeSpan.FromMinutes(30));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);

        await AssertCancelledRestockedRefunded(client, orderId, variant);
    }

    [Fact]
    public async Task Customer_cannot_cancel_after_the_hour()
    {
        var (client, _, orderId, _) = await PlacedOrder();
        await Age(orderId, TimeSpan.FromMinutes(61));

        Assert.False((await client.GetFromJsonAsync<Detail>($"/api/me/orders/{orderId}", Ct))!.CanCancel);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);
        Assert.Equal(PaymentStatus.Captured, await PaymentStatusOf(orderId));
    }

    [Fact]
    public async Task Customer_cannot_cancel_once_shipped()
    {
        var (client, _, orderId, _) = await PlacedOrder();
        await (await Admin()).PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Customer_cancel_racing_admin_ship_has_exactly_one_winner()
    {
        for (var round = 0; round < 5; round++)
        {
            var (client, _, orderId, variant) = await PlacedOrder(quantity: 1);
            var admin = await Admin();

            var responses = await Task.WhenAll(
                client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct),
                admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct));

            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
            var status = (await client.GetFromJsonAsync<Detail>($"/api/me/orders/{orderId}", Ct))!.Status;
            var stock = await factory.VariantOf(variant);
            // Restocked and refunded only when the cancel won.
            Assert.Equal(status == "Cancelled" ? 5 : 4, stock.StockOnHand);
            Assert.Equal(status == "Cancelled" ? PaymentStatus.Refunded : PaymentStatus.Captured, await PaymentStatusOf(orderId));
        }
    }

    [Fact]
    public async Task Admin_lists_orders_filtered_by_status()
    {
        var (_, _, paid, _) = await PlacedOrder();
        var (_, _, shipped, _) = await PlacedOrder();
        var admin = await Admin();
        await admin.PostAsync($"/api/admin/orders/{shipped}/ship", null, Ct);

        var onlyShipped = await admin.GetFromJsonAsync<Page>("/api/admin/orders?status=Shipped&pageSize=100", Ct);
        var all = await admin.GetFromJsonAsync<Page>("/api/admin/orders?pageSize=100", Ct);

        Assert.Contains(onlyShipped!.Items, o => o.Id == shipped);
        Assert.DoesNotContain(onlyShipped.Items, o => o.Id == paid);
        Assert.All(onlyShipped.Items, o => Assert.Equal("Shipped", o.Status));
        Assert.Contains(all!.Items, o => o.Id == paid);
    }

    [Fact]
    public async Task Admin_actions_are_audited()
    {
        var (_, _, first, _) = await PlacedOrder();
        var (_, _, second, _) = await PlacedOrder();
        var admin = await Admin();
        await admin.PostAsync($"/api/admin/orders/{first}/ship", null, Ct);
        await admin.PostAsync($"/api/admin/orders/{first}/deliver", null, Ct);
        await admin.PostAsync($"/api/admin/orders/{second}/cancel", null, Ct);

        var actions = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityId == first.ToString() || a.EntityId == second.ToString()).Select(a => a.Action).ToListAsync());

        Assert.Equal(["order.cancelled", "order.delivered", "order.shipped"], actions.Order());
    }

    [Fact]
    public async Task Account_deletion_removes_personal_details_but_keeps_totals()
    {
        var (_, customerId, orderId, _) = await PlacedOrder();
        var before = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));

        await factory.Query(async sp =>
        {
            await sp.GetRequiredService<Ashen.BuildingBlocks.Events.IEventBus>().Publish(new AccountDeletionRequested(customerId), Ct);
            return 0;
        });

        var after = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.DoesNotContain("Sam Lee", after.ShipTo.RecipientName);
        Assert.DoesNotContain("Main St", after.ShipTo.Line1);
        Assert.Equal("", after.ShipTo.Phone);
        Assert.Equal("", after.ContactEmail);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.ShipTo.StateCode, after.ShipTo.StateCode);
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/orders" },
        { "GET", $"/api/admin/orders/{Guid.Empty}" },
        { "POST", $"/api/admin/orders/{Guid.Empty}/ship" },
        { "POST", $"/api/admin/orders/{Guid.Empty}/deliver" },
        { "POST", $"/api/admin/orders/{Guid.Empty}/cancel" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Customers_are_forbidden_from_admin_order_routes(string method, string path)
    {
        var (client, _, _) = await Shopper.New(factory);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
