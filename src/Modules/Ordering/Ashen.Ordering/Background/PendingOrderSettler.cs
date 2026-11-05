using Ashen.BuildingBlocks.Background;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashen.Ordering.Background;

/// <summary>
/// Finishes order work that failed at the time (constitution VIII): every minute it retries placed orders whose
/// stock, coupon or payment isn't confirmed yet, and cancelled orders whose refund or restock is still pending.
/// The request that placed or cancelled the order normally does this itself.
/// </summary>
internal sealed partial class PendingOrderSettler(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<PendingOrderSettler> logger) : BackgroundService
{
    /// <summary>Leaves fresh orders to the request that made them, so the two never act at once.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        PeriodicJob.RunEveryMinute(clock, logger, SettlePending, stoppingToken);

    /// <returns>How many orders were fully settled.</returns>
    public async Task<int> SettlePending(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var completion = scope.ServiceProvider.GetRequiredService<OrderCompletion>();
        var cancellation = scope.ServiceProvider.GetRequiredService<OrderCancellation>();

        var cutoff = clock.GetUtcNow() - Grace;
        var pending = await db.Orders.Include(o => o.Lines)
            .Where(o => (o.CompletionPending && o.PlacedAt < cutoff)
                || (o.Status == OrderStatus.Cancelled && (o.RefundPending || o.RestockPending) && o.CancelledAt < cutoff))
            .ToListAsync(cancellationToken);

        var settled = 0;
        foreach (var order in pending)
        {
            try
            {
                if (order.CompletionPending)
                {
                    await completion.Complete(order, cancellationToken);
                }
                else
                {
                    await cancellation.Settle(order, cancellationToken);
                }

                LogSettled(order.Id);
                settled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogOrderFailed(ex, order.Id); // one bad order mustn't block the rest; it's tried again next minute
            }
        }

        return settled;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settled the pending work of order {OrderId}")]
    private partial void LogSettled(Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settling order {OrderId} failed again")]
    private partial void LogOrderFailed(Exception ex, Guid orderId);
}
