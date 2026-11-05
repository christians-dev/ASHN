using Ashen.BuildingBlocks;
using Ashen.Payments.Contracts;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;
using Ashen.Payments.Gateway;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ashen.Payments;

internal sealed partial class PaymentsModuleApi(
    PaymentsDbContext db, IPaymentGateway gateway, TimeProvider clock, ILogger<PaymentsModuleApi> logger) : IPaymentsModule
{
    public async Task<PaymentAuthorization> Authorize(
        Guid checkoutId, Guid customerId, Money amount, PaymentSource source, CancellationToken cancellationToken = default)
    {
        // Same checkout id → same answer, without charging twice.
        if (await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.CheckoutId == checkoutId, cancellationToken) is { } earlier)
        {
            return ToAuthorization(earlier);
        }

        var now = clock.GetUtcNow();
        var card = await CardFor(customerId, source, cancellationToken);
        Payment payment;
        if (!card.Succeeded)
        {
            payment = Payment.Declined(checkoutId, customerId, amount, card.Decline.ToString()!, null, null, now);
        }
        else
        {
            var result = await gateway.Authorize(amount, card.Token!, checkoutId.ToString());
            payment = result.Succeeded
                ? Payment.Authorized(checkoutId, customerId, amount, result.GatewayReference!, card.Brand!, card.Last4!, now)
                : Payment.Declined(checkoutId, customerId, amount, result.Decline.ToString()!, card.Brand, card.Last4, now);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        if (payment.Status == PaymentStatus.Declined)
        {
            LogDeclined(payment.Id, payment.DeclineReason);
        }
        else
        {
            LogAuthorized(payment.Id);
        }

        return ToAuthorization(payment);
    }

    public Task Capture(Guid paymentId, Guid orderId, CancellationToken cancellationToken = default) =>
        Change(paymentId, PaymentStatus.Captured, (p, key) => gateway.Capture(p.GatewayReference!, key), p => p.MarkCaptured(orderId, clock.GetUtcNow()), cancellationToken);

    public Task Void(Guid paymentId, CancellationToken cancellationToken = default) =>
        Change(paymentId, PaymentStatus.Voided, (p, key) => gateway.Void(p.GatewayReference!, key), p => p.MarkVoided(clock.GetUtcNow()), cancellationToken);

    public Task Refund(Guid paymentId, CancellationToken cancellationToken = default) =>
        Change(paymentId, PaymentStatus.Refunded, (p, key) => gateway.Refund(p.GatewayReference!, p.Amount, key), p => p.MarkRefunded(clock.GetUtcNow()), cancellationToken);

    public async Task<CardSummary?> GetCard(Guid customerId, Guid savedCardId, CancellationToken cancellationToken = default) =>
        await db.SavedCards.AsNoTracking()
            .Where(c => c.Id == savedCardId && c.CustomerId == customerId)
            .Select(c => new CardSummary(c.Brand, c.Last4, c.ExpMonth, c.ExpYear))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>A gateway token for the source: a new card is tokenised now, a saved card already has one.</summary>
    private async Task<TokenizeResult> CardFor(Guid customerId, PaymentSource source, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case NewCardSource card:
                return await gateway.SaveCard(new CardDetails(card.Number, card.ExpMonth, card.ExpYear, card.Cvc));
            case SavedCardSource saved:
                var found = await db.SavedCards.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == saved.SavedCardId && c.CustomerId == customerId, cancellationToken);
                return found is null
                    ? new TokenizeResult(false, null, null, null, 0, 0, DeclineCode.InvalidCard)
                    : new TokenizeResult(true, found.GatewayToken, found.Brand, found.Last4, found.ExpMonth, found.ExpYear, null);
            default:
                throw new NotSupportedException($"Payment source {source.GetType().Name} is not supported.");
        }
    }

    /// <summary>
    /// Moves a payment to <paramref name="target"/> once. Callers retry after failures (checkout completion,
    /// cancellation), so a payment already there is left alone, and the gateway gets a key that's the same on every
    /// try: if it acted but our save failed, the retry gets its earlier answer instead of moving money twice.
    /// </summary>
    private async Task Change(
        Guid paymentId, PaymentStatus target, Func<Payment, string, Task<GatewayResult>> callGateway, Action<Payment> record, CancellationToken cancellationToken)
    {
        var what = target.ToString().ToLowerInvariant();
        var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
        if (payment.Status == target)
        {
            return;
        }

        var result = await callGateway(payment, $"{paymentId:N}-{what}");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The payment gateway refused to mark payment {paymentId} {what}.");
        }

        record(payment);
        await db.SaveChangesAsync(cancellationToken);
        LogChanged(paymentId, what);
    }

    private static PaymentAuthorization ToAuthorization(Payment p) => new(
        p.Status is PaymentStatus.Authorized or PaymentStatus.Captured, // a voided or declined attempt never counts as approved
        p.Id,
        p.DeclineReason,
        p.CardBrand is null || p.CardLast4 is null ? null : new CardSummary(p.CardBrand, p.CardLast4, 0, 0));

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} authorized")]
    private partial void LogAuthorized(Guid paymentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} declined: {Reason}")]
    private partial void LogDeclined(Guid paymentId, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} {Change}")]
    private partial void LogChanged(Guid paymentId, string change);
}
