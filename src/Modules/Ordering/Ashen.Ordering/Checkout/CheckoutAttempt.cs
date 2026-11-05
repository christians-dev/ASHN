namespace Ashen.Ordering.Checkout;

/// <summary>A checkout id some request is placing right now (see <see cref="CheckoutOrchestrator"/>).</summary>
internal sealed class CheckoutAttempt
{
    public Guid CheckoutId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
}
