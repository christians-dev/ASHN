using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Customers.Domain;

/// <summary>A delivery address as typed by the customer, before it is stored.</summary>
internal sealed record AddressDetails(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string StateCode,
    string PostalCode,
    string CountryCode,
    string Phone);

/// <summary>The shopping side of an account: its addresses and wishlist. Same id as the account.</summary>
internal sealed class CustomerProfile : AggregateRoot
{
    private readonly List<Address> _addresses = [];
    private readonly List<WishlistItem> _wishlist = [];

    private CustomerProfile() { }

    public CustomerProfile(Guid customerId, DateTimeOffset now)
    {
        Id = customerId;
        CreatedAt = now;
    }

    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<Address> Addresses => _addresses;
    public IReadOnlyList<WishlistItem> Wishlist => _wishlist;

    /// <summary>The first address becomes the default (FR-016).</summary>
    public Address AddAddress(AddressDetails details)
    {
        var address = new Address(details, isDefault: _addresses.Count == 0);
        _addresses.Add(address);
        return address;
    }

    public Result UpdateAddress(Guid addressId, AddressDetails details)
    {
        if (Find(addressId) is not { } address)
        {
            return NotFound;
        }

        address.Update(details);
        return Result.Success();
    }

    /// <summary>Removing the default makes the oldest remaining address the default, so one always is.</summary>
    public Result RemoveAddress(Guid addressId)
    {
        if (Find(addressId) is not { } address)
        {
            return NotFound;
        }

        _addresses.Remove(address);
        if (address.IsDefault && _addresses.Count > 0)
        {
            _addresses.OrderBy(a => a.Id).First().MarkDefault(true);
        }

        return Result.Success();
    }

    public Result SetDefault(Guid addressId)
    {
        if (Find(addressId) is not { } address)
        {
            return NotFound;
        }

        foreach (var other in _addresses)
        {
            other.MarkDefault(other == address);
        }

        return Result.Success();
    }

    /// <summary>Saving a product twice keeps the one entry (FR-041).</summary>
    public void AddToWishlist(Guid productId, DateTimeOffset now)
    {
        if (_wishlist.All(i => i.ProductId != productId))
        {
            _wishlist.Add(new WishlistItem(productId, now));
        }
    }

    public void RemoveFromWishlist(Guid productId) => _wishlist.RemoveAll(i => i.ProductId == productId);

    private Address? Find(Guid id) => _addresses.SingleOrDefault(a => a.Id == id);

    private static Error NotFound => Error.NotFound("address.not_found", "Address not found.");
}

internal sealed class Address : Entity
{
    private Address() { }

    internal Address(AddressDetails details, bool isDefault)
    {
        Update(details);
        IsDefault = isDefault;
    }

    public string RecipientName { get; private set; } = "";
    public string Line1 { get; private set; } = "";
    public string? Line2 { get; private set; }
    public string City { get; private set; } = "";
    public string StateCode { get; private set; } = "";
    public string PostalCode { get; private set; } = "";
    public string CountryCode { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public bool IsDefault { get; private set; }

    internal void Update(AddressDetails d)
    {
        RecipientName = d.RecipientName.Trim();
        Line1 = d.Line1.Trim();
        Line2 = string.IsNullOrWhiteSpace(d.Line2) ? null : d.Line2.Trim();
        City = d.City.Trim();
        StateCode = d.StateCode.Trim().ToUpperInvariant();
        PostalCode = d.PostalCode.Trim();
        CountryCode = d.CountryCode.Trim().ToUpperInvariant();
        Phone = d.Phone.Trim();
    }

    internal void MarkDefault(bool isDefault) => IsDefault = isDefault;
}

/// <summary>A product saved for later (User Story 11).</summary>
internal sealed class WishlistItem : Entity
{
    private WishlistItem() { }

    internal WishlistItem(Guid productId, DateTimeOffset addedAt)
    {
        ProductId = productId;
        AddedAt = addedAt;
    }

    public Guid ProductId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
}
