namespace Ashen.Ordering.Contracts;

/// <summary>What other modules may ask Ordering (module-contracts.md).</summary>
public interface IOrderingModule
{
    /// <summary>Whether the customer has a Delivered order containing the product: only they may review it (FR-039).</summary>
    Task<bool> HasDeliveredProduct(Guid customerId, Guid productId, CancellationToken cancellationToken = default);
}
