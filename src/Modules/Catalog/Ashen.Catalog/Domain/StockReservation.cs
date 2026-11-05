using Ashen.BuildingBlocks.Domain;

namespace Ashen.Catalog.Domain;

internal enum ReservationStatus
{
    Pending,
    Confirmed,
    Released,
}

/// <summary>
/// Stock held for one checkout while it is being paid for. Created together with <c>StockReserved += qty</c>,
/// only when that much is available; confirmed when the order is saved, released if checkout fails or expires.
/// </summary>
internal sealed class StockReservation : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private StockReservation() { }

    public StockReservation(Guid checkoutId, Guid variantId, int quantity, DateTimeOffset now)
    {
        CheckoutId = checkoutId;
        VariantId = variantId;
        Quantity = quantity;
        Status = ReservationStatus.Pending;
        ExpiresAt = now + Lifetime;
    }

    public Guid CheckoutId { get; private set; }
    public Guid VariantId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public void Confirm() => Status = ReservationStatus.Confirmed;
    public void Release() => Status = ReservationStatus.Released;
}
