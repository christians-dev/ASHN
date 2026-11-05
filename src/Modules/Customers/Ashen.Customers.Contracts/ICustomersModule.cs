namespace Ashen.Customers.Contracts;

/// <summary>What other modules may ask Customers (module-contracts.md).</summary>
public interface ICustomersModule
{
    /// <summary>The customer's own address, or null if it doesn't exist or belongs to someone else.</summary>
    Task<AddressSnapshot?> GetAddress(Guid customerId, Guid addressId, CancellationToken cancellationToken = default);
}

/// <summary>An address as it was at one moment; orders keep this copy even if the customer later edits it.</summary>
public sealed record AddressSnapshot(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string StateCode,
    string PostalCode,
    string CountryCode,
    string Phone);
