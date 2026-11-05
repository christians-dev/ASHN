using Ashen.Catalog.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Payments.Contracts;
using Ashen.Promotions.Contracts;

namespace Ashen.Ordering.Shared;

/// <summary>
/// The last checkout step, once the order is saved: take the stock and the coupon use for good, and capture the
/// money. Every call it makes is idempotent, so <see cref="Background.PendingOrderSettler"/> can safely repeat it.
/// </summary>
internal sealed class OrderCompletion(OrderingDbContext db, ICatalogModule catalog, IPromotionsModule promotions, IPaymentsModule payments)
{
    // shortcut: the stock hold and the authorisation also expire on their own after 15 minutes; the settler retries
    // every minute, so it wins by a wide margin. If Payments or Catalog can be down for longer, have those safety
    // nets ask Ordering whether an order exists first.
    public async Task Complete(Order order, CancellationToken cancellationToken)
    {
        await catalog.ConfirmStock(order.CheckoutId, cancellationToken);
        await promotions.ConfirmCoupon(order.CheckoutId, order.Id, cancellationToken);
        await payments.Capture(order.PaymentId, order.Id, cancellationToken);
        order.MarkCompleted();
        await db.SaveChangesAsync(cancellationToken);
    }
}
