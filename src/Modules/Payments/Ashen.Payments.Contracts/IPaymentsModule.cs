using Ashen.BuildingBlocks;

namespace Ashen.Payments.Contracts;

/// <summary>What Ordering may ask Payments (module-contracts.md).</summary>
public interface IPaymentsModule
{
    /// <summary>Reserves the money on the card. Idempotent per <paramref name="checkoutId"/>: a repeat returns the first answer.</summary>
    Task<PaymentAuthorization> Authorize(Guid checkoutId, Guid customerId, Money amount, PaymentSource source, CancellationToken cancellationToken = default);

    /// <summary>Takes the authorised money and links the payment to its order.</summary>
    Task Capture(Guid paymentId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Releases an authorisation that never became an order (FR-021).</summary>
    Task Void(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Gives back the full captured amount (cancelled orders, FR-036).</summary>
    Task Refund(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>The customer's saved card, or null if they have no such card (another customer's card counts as none).</summary>
    Task<CardSummary?> GetCard(Guid customerId, Guid savedCardId, CancellationToken cancellationToken = default);
}

public abstract record PaymentSource;

/// <summary>Card details typed at checkout. Used once to tokenise and never stored or logged.</summary>
public sealed record NewCardSource(string Number, int ExpMonth, int ExpYear, string Cvc) : PaymentSource
{
    // Keep the number and CVC out of logs and exception messages.
    public override string ToString() => "NewCardSource { ** redacted ** }";
}

/// <summary>A card the customer saved earlier (it must be theirs).</summary>
public sealed record SavedCardSource(Guid SavedCardId) : PaymentSource;

public sealed record CardSummary(string Brand, string Last4, int ExpMonth, int ExpYear);

public sealed record PaymentAuthorization(bool Approved, Guid? PaymentId, string? DeclineReason, CardSummary? Card);
