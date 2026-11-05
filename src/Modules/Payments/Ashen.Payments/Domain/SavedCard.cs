using Ashen.BuildingBlocks.Domain;

namespace Ashen.Payments.Domain;

/// <summary>
/// A card kept for later (FR-027, FR-029): the gateway's token plus what the customer needs to recognise it.
/// Never the full number or CVC; those are handed to the gateway once and dropped.
/// </summary>
internal sealed class SavedCard : Entity
{
    private SavedCard() { }

    public static SavedCard Create(Guid customerId, string gatewayToken, string brand, string last4, int expMonth, int expYear, DateTimeOffset now) =>
        new()
        {
            CustomerId = customerId,
            GatewayToken = gatewayToken,
            Brand = brand,
            Last4 = last4,
            ExpMonth = expMonth,
            ExpYear = expYear,
            CreatedAt = now,
        };

    public Guid CustomerId { get; private set; }
    public string GatewayToken { get; private set; } = "";
    public string Brand { get; private set; } = "";
    public string Last4 { get; private set; } = "";
    public int ExpMonth { get; private set; }
    public int ExpYear { get; private set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A card works through the last day of its expiry month.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpYear < now.Year || (ExpYear == now.Year && ExpMonth < now.Month);
}
