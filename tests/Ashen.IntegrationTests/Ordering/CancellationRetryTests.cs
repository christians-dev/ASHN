using System.Net;

using Ashen.IntegrationTests.Infrastructure;
using Ashen.Ordering.Background;
using Ashen.Ordering.Data;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ashen.IntegrationTests.Ordering;

/// <summary>
/// Makes saving a payment that has just reached <see cref="FailSaving"/> fail, as if Payments went down after the
/// gateway answered. That is the worst moment: the provider acted, our record didn't.
/// </summary>
public sealed class FailingPaymentSaveFactory : CheckoutFactory
{
    internal PaymentStatus? FailSaving { get; set; }

    private sealed class ThrowOnStatus(FailingPaymentSaveFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            factory.FailSaving is { } status && eventData.Context!.ChangeTracker.Entries<Payment>().Any(e => e.State == EntityState.Modified && e.Entity.Status == status)
                ? throw new InvalidOperationException($"Simulated failure while saving a {factory.FailSaving} payment.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.ConfigureDbContext<PaymentsDbContext>(o => o.AddInterceptors(new ThrowOnStatus(this))));
    }
}

/// <summary>Order work that fails at the time is finished later by the settler (constitution VIII).</summary>
public class CancellationRetryTests(FailingPaymentSaveFactory factory) : IClassFixture<FailingPaymentSaveFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_refund_is_retried_until_the_order_is_refunded_and_restocked()
    {
        var variant = await factory.CreateProduct("Retry Tee", 20m, stock: 5);
        var (client, _, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 2);
        var orderId = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;

        factory.FailSaving = PaymentStatus.Refunded;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);

        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.True(order.RefundPending && order.RestockPending); // cancelled, with the unfinished work recorded
        Assert.Equal(PaymentStatus.Captured, await PaymentStatusOf(orderId));

        factory.FailSaving = null;
        Assert.True(await Settler().SettlePending(Ct) >= 1);
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusOf(orderId));
        Assert.Equal(5, (await factory.VariantOf(variant)).StockOnHand);
        order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.False(order.RefundPending || order.RestockPending);
    }

    [Fact]
    public async Task Failed_capture_is_retried_and_the_order_cannot_be_cancelled_meanwhile()
    {
        var variant = await factory.CreateProduct("Capture Retry Tee", 20m, stock: 5);
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 2);

        factory.FailSaving = PaymentStatus.Captured;
        var placed = await client.Checkout(addressId, Shopper.GoodCard);
        factory.FailSaving = null;

        Assert.Equal(HttpStatusCode.Created, placed.StatusCode); // the customer has their order
        var orderId = (await placed.ReadAs<Shopper.Placed>()).OrderId;
        var payment = await factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().SingleAsync(p => p.CustomerId == customerId));
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);

        Assert.True(await Settler().SettlePending(Ct) >= 1);

        Assert.Equal(PaymentStatus.Captured, await PaymentStatusOf(orderId));
        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.False(order.CompletionPending);
        Assert.Equal((3, 0), ((await factory.VariantOf(variant)).StockOnHand, (await factory.VariantOf(variant)).StockReserved));
    }

    private PendingOrderSettler Settler() => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new FixedClock(DateTimeOffset.UtcNow + PendingOrderSettler.Grace + TimeSpan.FromMinutes(1)),
        NullLogger<PendingOrderSettler>.Instance);

    private Task<PaymentStatus> PaymentStatusOf(Guid orderId) =>
        factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.Where(p => p.OrderId == orderId).Select(p => p.Status).SingleAsync());
}
