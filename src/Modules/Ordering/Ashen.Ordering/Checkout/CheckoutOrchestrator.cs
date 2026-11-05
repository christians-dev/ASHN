using Ashen.BuildingBlocks;
using Ashen.Catalog.Contracts;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;
using Ashen.Payments.Contracts;
using Ashen.Promotions.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ashen.Ordering.Checkout;

internal sealed record PlacedOrder(Guid OrderId, string OrderNumber);

/// <summary>
/// Places an order across modules without a shared transaction (research R7): reserve stock → reserve coupon →
/// authorise payment → save order → confirm stock and coupon → capture. Any failure undoes the earlier steps in reverse, so no variant is
/// over-sold and nobody is charged without an order (FR-020, FR-021).
/// </summary>
internal sealed partial class CheckoutOrchestrator(
    OrderingDbContext db,
    CartResolver carts,
    CheckoutPricing pricing,
    ICatalogModule catalog,
    IPaymentsModule payments,
    IPromotionsModule promotions,
    OrderCompletion completion,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<CheckoutOrchestrator> logger)
{
    /// <param name="checkoutId">The client's idempotency key: a repeat returns the first order instead of buying again.</param>
    public async Task<Result<PlacedOrder>> Place(Guid checkoutId, Guid addressId, PaymentSource paymentSource, CancellationToken cancellationToken)
    {
        var customerId = user.UserId ?? throw new InvalidOperationException("Checkout needs a signed-in customer.");

        // A double click sends two requests with one checkout id. Unguarded, both would pass the "already placed?"
        // check and share one stock hold and one payment, and the loser's clean-up would void the winner's payment.
        // So one request claims the id (a row, not a held lock or connection) and the other waits, then finds its order.
        for (var waited = TimeSpan.Zero; ; waited += ClaimPoll)
        {
            if (await TryClaim(checkoutId, cancellationToken))
            {
                try
                {
                    return await PlaceClaimed(checkoutId, customerId, addressId, paymentSource, cancellationToken);
                }
                finally
                {
                    await db.Database.ExecuteSqlAsync(
                        $"DELETE FROM ordering.checkout_attempts WHERE checkout_id = {checkoutId}", CancellationToken.None);
                }
            }

            if (waited >= ClaimWait)
            {
                return Error.Conflict("checkout.in_progress", "This order is still being placed. Check your orders in a moment.");
            }

            await Task.Delay(ClaimPoll, clock, cancellationToken);
        }
    }

    private static readonly TimeSpan ClaimPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClaimWait = TimeSpan.FromSeconds(15);

    /// <summary>A claim this old belongs to a request that crashed; checkouts take seconds.</summary>
    private static readonly TimeSpan ClaimAbandoned = TimeSpan.FromMinutes(2);

    /// <summary>Claims the checkout id unless a live request holds it. One statement, so two can't both win.</summary>
    private async Task<bool> TryClaim(Guid checkoutId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var abandoned = now - ClaimAbandoned;
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ordering.checkout_attempts (checkout_id, claimed_at) VALUES ({checkoutId}, {now})
            ON CONFLICT (checkout_id) DO UPDATE SET claimed_at = excluded.claimed_at
            WHERE checkout_attempts.claimed_at < {abandoned}
            """, cancellationToken) == 1;
    }

    private async Task<Result<PlacedOrder>> PlaceClaimed(
        Guid checkoutId, Guid customerId, Guid addressId, PaymentSource paymentSource, CancellationToken cancellationToken)
    {

        if (await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.CheckoutId == checkoutId, cancellationToken) is { } earlier)
        {
            return earlier.CustomerId == customerId
                ? new PlacedOrder(earlier.Id, earlier.OrderNumber)
                : Error.Conflict("checkout.key_used", "This checkout attempt was already used. Please try again.");
        }

        var cart = await carts.Find(cancellationToken);
        var priced = await pricing.Price(cart, customerId, addressId, cancellationToken);
        if (!priced.IsSuccess)
        {
            return priced.Error!;
        }

        var checkout = priced.Value;
        if (checkout.Problems.Count > 0)
        {
            return Error.Conflict("checkout.cart_changed", string.Join(" ", checkout.Problems));
        }

        // 1. Hold the stock.
        var reserved = await catalog.ReserveStock(checkoutId, checkout.Lines.Select(l => new StockLine(l.Variant.VariantId, l.Quantity)).ToList(), cancellationToken);
        if (!reserved.IsSuccess)
        {
            return reserved.Error!;
        }

        // 2. Hold one use of the coupon. Another checkout may have just taken its last use.
        if (checkout.CouponCode is { } couponCode)
        {
            var held = await promotions.ReserveCoupon(couponCode, customerId, checkoutId, checkout.Subtotal, cancellationToken);
            if (!held.IsSuccess || held.Value != checkout.Discount)
            {
                await promotions.ReleaseCoupon(checkoutId, CancellationToken.None);
                await catalog.ReleaseStock(checkoutId, CancellationToken.None);
                return Error.Conflict("checkout.coupon", $"Coupon {couponCode}: {held.Error?.Message ?? "the discount changed."} Remove it or try again.");
            }
        }

        // 3. Authorise the payment.
        var payment = await payments.Authorize(checkoutId, customerId, checkout.Total, paymentSource, cancellationToken);
        if (!payment.Approved)
        {
            await promotions.ReleaseCoupon(checkoutId, CancellationToken.None);
            await catalog.ReleaseStock(checkoutId, cancellationToken);
            LogDeclined(checkoutId, payment.DeclineReason);
            return Error.Conflict("payment.declined", DeclineMessage(payment.DeclineReason));
        }

        // 4. Save the order. If this fails, the customer must not be charged and the stock must go back on sale.
        Order order;
        try
        {
            order = Order.Place(
                await db.NextOrderNumber(cancellationToken),
                checkoutId,
                customerId,
                user.Email ?? "",
                checkout.Lines.Select(l => new OrderLine(
                    l.Variant.ProductId, l.Variant.VariantId, l.Variant.ProductName, l.Variant.Sku,
                    l.Variant.OptionValues.Select(o => new OptionValueSnapshot(o.Key, o.Value)).ToList(),
                    l.Variant.Price, l.Quantity)).ToList(),
                checkout.CouponCode,
                checkout.Discount,
                checkout.Shipping,
                checkout.TaxRate,
                checkout.TaxJurisdiction,
                checkout.Tax,
                new ShipTo(checkout.Address.RecipientName, checkout.Address.Line1, checkout.Address.Line2, checkout.Address.City,
                    checkout.Address.StateCode, checkout.Address.PostalCode, checkout.Address.CountryCode, checkout.Address.Phone),
                payment.PaymentId!.Value,
                payment.Card?.Brand ?? "",
                payment.Card?.Last4 ?? "",
                clock.GetUtcNow());
            db.Orders.Add(order);
            cart!.Clear(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex, checkoutId);
            db.ChangeTracker.Clear();
            await payments.Void(payment.PaymentId!.Value, CancellationToken.None);
            await promotions.ReleaseCoupon(checkoutId, CancellationToken.None);
            await catalog.ReleaseStock(checkoutId, CancellationToken.None);
            throw;
        }

        // 5. The order exists: take the stock and the coupon use for good, and the money. The order was saved as
        // CompletionPending, so if this fails the background settler finishes it; the customer has their order.
        try
        {
            await completion.Complete(order, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogCompletionFailed(ex, order.Id);
        }

        LogPlaced(order.Id, order.OrderNumber);
        return new PlacedOrder(order.Id, order.OrderNumber);
    }

    private static string DeclineMessage(string? reason) => reason switch
    {
        "InsufficientFunds" => "Your card was declined for insufficient funds. Try another card.",
        "ExpiredCard" => "Your card has expired. Try another card.",
        "InvalidCard" => "Those card details aren't valid. Check the number, expiry date and security code.",
        _ => "Your card was declined. Try another card.",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed as {OrderNumber}")]
    private partial void LogPlaced(Guid orderId, string orderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was placed but confirming stock, coupon or payment failed; it will be retried")]
    private partial void LogCompletionFailed(Exception ex, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checkout {CheckoutId} payment declined: {Reason}")]
    private partial void LogDeclined(Guid checkoutId, string? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the order for checkout {CheckoutId} failed after payment approval; voiding the payment and releasing stock")]
    private partial void LogSaveFailed(Exception ex, Guid checkoutId);
}
