using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashen.Ordering.Background;

/// <summary>
/// Finishes cancellations whose refund or restock failed at the time (constitution VIII): every minute it retries
/// cancelled orders that still have either pending. The cancelling request normally does both itself.
/// </summary>
internal sealed partial class PendingCancellationSettler(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<PendingCancellationSettler> logger) : BackgroundService
{
    /// <summary>Leaves fresh cancellations to the request that made them, so the two never refund at once.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SettlePending(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    /// <returns>How many orders were fully settled.</returns>
    public async Task<int> SettlePending(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var cancellation = scope.ServiceProvider.GetRequiredService<OrderCancellation>();

        var cutoff = clock.GetUtcNow() - Grace;
        var pending = await db.Orders.Include(o => o.Lines)
            .Where(o => o.Status == OrderStatus.Cancelled && (o.RefundPending || o.RestockPending) && o.CancelledAt < cutoff)
            .ToListAsync(cancellationToken);

        var settled = 0;
        foreach (var order in pending)
        {
            try
            {
                await cancellation.Settle(order, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settled the refund and restock of cancelled order {OrderId}")]
    private partial void LogSettled(Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settling cancelled order {OrderId} failed again")]
    private partial void LogOrderFailed(Exception ex, Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settling pending cancellations failed")]
    private partial void LogFailed(Exception ex);
}
