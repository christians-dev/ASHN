using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.Catalog.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Payments.Contracts;

using Microsoft.Extensions.Logging;

namespace Ashen.Ordering.Shared;

/// <summary>
/// Cancels a Paid order, for the customer (within the hour) or an admin (FR-035, FR-036). The status change is
/// saved first under the order's concurrency token, so a cancel racing a ship has exactly one winner; only the
/// winner refunds and restocks. That save also records both as pending, so if either fails now,
/// <see cref="Background.PendingCancellationSettler"/> finishes them later (constitution VIII).
/// </summary>
internal sealed partial class OrderCancellation(
    OrderingDbContext db, ICatalogModule catalog, IPaymentsModule payments, IAuditLog audit, TimeProvider clock,
    ILogger<OrderCancellation> logger)
{
    public async Task<Result> Cancel(Order order, bool byAdmin, CancellationToken cancellationToken)
    {
        var cancelled = order.Cancel(clock.GetUtcNow());
        if (!cancelled.IsSuccess)
        {
            return cancelled;
        }

        if (byAdmin)
        {
            audit.Record(db, "order.cancelled", nameof(Order), order.Id.ToString());
        }

        var saved = await db.TrySaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        try
        {
            await Settle(order, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The order is cancelled and the rest is recorded as pending; the background settler retries it.
            LogSettleFailed(ex, order.Id);
        }

        return Result.Success();
    }

    /// <summary>Refunds (money first) and restocks whatever is still pending, saving after each step.</summary>
    public async Task Settle(Order order, CancellationToken cancellationToken)
    {
        if (order.RefundPending)
        {
            await payments.Refund(order.PaymentId, cancellationToken); // idempotent, so a retry after a crash here is safe
            order.MarkRefunded();
            await db.SaveChangesAsync(cancellationToken);
        }

        if (order.RestockPending)
        {
            // shortcut: a crash between the restock and this save restocks twice on retry (stock too high, never
            // money); record restocks per order in Catalog if that ever matters.
            await catalog.RestockForCancelledOrder([.. order.Lines.Select(l => new StockLine(l.VariantId, l.Quantity))], cancellationToken);
            order.MarkRestocked();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} is cancelled but its refund or restock failed; it will be retried")]
    private partial void LogSettleFailed(Exception ex, Guid orderId);
}
