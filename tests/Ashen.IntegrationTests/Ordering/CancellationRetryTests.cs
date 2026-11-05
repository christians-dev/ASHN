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

/// <summary>Makes recording a refund fail while <see cref="FailRefunds"/> is set, as if Payments were down.</summary>
public sealed class FailingRefundFactory : CheckoutFactory
{
    public volatile bool FailRefunds;

    private sealed class ThrowOnRefund(FailingRefundFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            factory.FailRefunds && eventData.Context!.ChangeTracker.Entries<Payment>().Any(e => e.Entity.Status == PaymentStatus.Refunded)
                ? throw new InvalidOperationException("Simulated failure while refunding.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.ConfigureDbContext<PaymentsDbContext>(o => o.AddInterceptors(new ThrowOnRefund(this))));
    }
}

/// <summary>A cancellation whose refund fails is still refunded and restocked later (constitution VIII).</summary>
public class CancellationRetryTests(FailingRefundFactory factory) : IClassFixture<FailingRefundFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_refund_is_retried_until_the_order_is_refunded_and_restocked()
    {
        var variant = await factory.CreateProduct("Retry Tee", 20m, stock: 5);
        var (client, _, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 2);
        var orderId = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;

        factory.FailRefunds = true;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/orders/{orderId}/cancel", null, Ct)).StatusCode);

        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.True(order.RefundPending && order.RestockPending); // cancelled, with the unfinished work recorded
        Assert.Equal(PaymentStatus.Captured, await PaymentStatusOf(orderId));

        factory.FailRefunds = false;
        var settler = new PendingCancellationSettler(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(DateTimeOffset.UtcNow + PendingCancellationSettler.Grace + TimeSpan.FromMinutes(1)),
            NullLogger<PendingCancellationSettler>.Instance);

        Assert.True(await settler.SettlePending(Ct) >= 1);
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusOf(orderId));
        Assert.Equal(5, (await factory.VariantOf(variant)).StockOnHand);
        order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.False(order.RefundPending || order.RestockPending);
    }

    private Task<PaymentStatus> PaymentStatusOf(Guid orderId) =>
        factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().Payments.Where(p => p.OrderId == orderId).Select(p => p.Status).SingleAsync());
}
