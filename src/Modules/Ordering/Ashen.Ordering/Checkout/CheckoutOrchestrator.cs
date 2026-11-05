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
    ICurrentUser user,
    TimeProvider clock,
    ILogger<CheckoutOrchestrator> logger)
{
    /// <param name="checkoutId">The client's idempotency key: a repeat returns the first order instead of buying again.</param>
    public async Task<Result<PlacedOrder>> Place(Guid checkoutId, Guid addressId, PaymentSource paymentSource, CancellationToken cancellationToken)
    {
        var customerId = user.UserId ?? throw new InvalidOperationException("Checkout needs a signed-in customer.");

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

        // 5. The order exists: take the stock and the coupon use for good, and the money.
        await catalog.ConfirmStock(checkoutId, CancellationToken.None);
        await promotions.ConfirmCoupon(checkoutId, order.Id, CancellationToken.None);
        await payments.Capture(payment.PaymentId.Value, order.Id, CancellationToken.None);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Checkout {CheckoutId} payment declined: {Reason}")]
    private partial void LogDeclined(Guid checkoutId, string? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the order for checkout {CheckoutId} failed after payment approval; voiding the payment and releasing stock")]
    private partial void LogSaveFailed(Exception ex, Guid checkoutId);
}
