using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Ordering.Domain;

/// <summary>What a shopper means to buy. Belongs to a customer, or to a guest via a cookie token (FR-018).</summary>
internal sealed class Cart : AggregateRoot
{
    public const int MaxQuantity = 99;

    private readonly List<CartItem> _items = [];

    private Cart() { }

    public static Cart ForCustomer(Guid customerId, DateTimeOffset now) => new() { CustomerId = customerId, UpdatedAt = now };
    public static Cart ForGuest(string anonymousToken, DateTimeOffset now) => new() { AnonymousToken = anonymousToken, UpdatedAt = now };

    public Guid? CustomerId { get; private set; }
    public string? AnonymousToken { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The one coupon applied to this cart (FR-038); re-checked whenever the cart is priced.</summary>
    public string? CouponCode { get; private set; }
    public IReadOnlyList<CartItem> Items => _items;

    /// <summary>Sets a line's quantity; 0 removes it. Holds no stock: stock is only claimed at checkout.</summary>
    public Result SetQuantity(Guid variantId, int quantity, Money currentPrice, DateTimeOffset now)
    {
        if (quantity is < 0 or > MaxQuantity)
        {
            return Error.Validation("cart.quantity", $"Quantity must be between 0 and {MaxQuantity}.");
        }

        var item = _items.SingleOrDefault(i => i.VariantId == variantId);
        if (quantity == 0)
        {
            if (item is not null)
            {
                _items.Remove(item);
            }
        }
        else if (item is null)
        {
            _items.Add(new CartItem(variantId, quantity, currentPrice));
        }
        else
        {
            item.Quantity = quantity;
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Brings a guest cart's lines into this one after sign-in; quantities add up, capped at 99.</summary>
    public void MergeFrom(Cart guest, DateTimeOffset now)
    {
        foreach (var line in guest.Items)
        {
            var item = _items.SingleOrDefault(i => i.VariantId == line.VariantId);
            if (item is null)
            {
                _items.Add(new CartItem(line.VariantId, line.Quantity, line.PriceWhenAdded));
            }
            else
            {
                item.Quantity = Math.Min(MaxQuantity, item.Quantity + line.Quantity);
            }
        }

        UpdatedAt = now;
    }

    public void Clear(DateTimeOffset now)
    {
        _items.Clear();
        CouponCode = null;
        UpdatedAt = now;
    }

    /// <summary>Applies a coupon, replacing any earlier one: one coupon per order.</summary>
    public void ApplyCoupon(string code, DateTimeOffset now)
    {
        CouponCode = code;
        UpdatedAt = now;
    }

    public void RemoveCoupon(DateTimeOffset now)
    {
        CouponCode = null;
        UpdatedAt = now;
    }
}

internal sealed class CartItem : Entity
{
    private CartItem() { }

    internal CartItem(Guid variantId, int quantity, Money priceWhenAdded)
    {
        VariantId = variantId;
        Quantity = quantity;
        PriceWhenAdded = priceWhenAdded;
    }

    public Guid VariantId { get; private set; }
    public int Quantity { get; internal set; }

    /// <summary>Lets the cart tell the shopper a price changed since they added the item.</summary>
    public Money PriceWhenAdded { get; private set; }
}
