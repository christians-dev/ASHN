using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Payments.Domain;

internal enum PaymentStatus
{
    Authorized,
    Captured,
    Voided,
    Refunded,
    Declined,
}

/// <summary>One payment attempt for one checkout. Holds only a gateway reference and card summary, never card data.</summary>
internal sealed class Payment : AggregateRoot
{
    private Payment() { }

    public static Payment Authorized(Guid checkoutId, Guid customerId, Money amount, string gatewayReference, string brand, string last4, DateTimeOffset now) =>
        new()
        {
            CheckoutId = checkoutId,
            CustomerId = customerId,
            Amount = amount,
            GatewayReference = gatewayReference,
            CardBrand = brand,
            CardLast4 = last4,
            Status = PaymentStatus.Authorized,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public static Payment Declined(Guid checkoutId, Guid customerId, Money amount, string reason, string? brand, string? last4, DateTimeOffset now) =>
        new()
        {
            CheckoutId = checkoutId,
            CustomerId = customerId,
            Amount = amount,
            DeclineReason = reason,
            CardBrand = brand,
            CardLast4 = last4,
            Status = PaymentStatus.Declined,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public Guid CheckoutId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Money Amount { get; private set; }
    public string? GatewayReference { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? DeclineReason { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCaptured(Guid orderId, DateTimeOffset now) => Move(PaymentStatus.Authorized, PaymentStatus.Captured, now, () => OrderId = orderId);
    public void MarkVoided(DateTimeOffset now) => Move(PaymentStatus.Authorized, PaymentStatus.Voided, now);
    public void MarkRefunded(DateTimeOffset now) => Move(PaymentStatus.Captured, PaymentStatus.Refunded, now);

    private void Move(PaymentStatus from, PaymentStatus to, DateTimeOffset now, Action? also = null)
    {
        if (Status != from)
        {
            throw new InvalidOperationException($"A {Status} payment cannot become {to}.");
        }

        also?.Invoke();
        Status = to;
        UpdatedAt = now;
    }
}
