using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;
using Ashen.Ordering.Contracts;

namespace Ashen.Ordering.Domain;

internal enum OrderStatus
{
    Paid,
    Shipped,
    Delivered,
    Cancelled,
}

/// <summary>The delivery address as it was when the order was placed.</summary>
internal sealed record ShipTo(string RecipientName, string Line1, string? Line2, string City, string StateCode, string PostalCode, string CountryCode, string Phone);

internal sealed record OptionValueSnapshot(string Name, string Value);

/// <summary>
/// A paid purchase. Everything that can change later (prices, names, address, tax rate, discount) is copied
/// in when it is placed, so the order always shows what the customer actually bought and paid (FR-022).
/// </summary>
internal sealed class Order : AggregateRoot
{
    public static readonly TimeSpan CustomerCancelWindow = TimeSpan.FromHours(1);

    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public static Order Place(
        string orderNumber,
        Guid checkoutId,
        Guid customerId,
        string contactEmail,
        IReadOnlyList<OrderLine> lines,
        string? couponCode,
        Money discount,
        Money shipping,
        decimal taxRate,
        string taxJurisdiction,
        Money tax,
        ShipTo shipTo,
        Guid paymentId,
        string cardBrand,
        string cardLast4,
        DateTimeOffset placedAt)
    {
        var currency = discount.Currency;
        var subtotal = lines.Aggregate(Money.Zero(currency), (sum, line) => sum + line.LineTotal);
        var order = new Order
        {
            OrderNumber = orderNumber,
            CheckoutId = checkoutId,
            CustomerId = customerId,
            ContactEmail = contactEmail,
            Status = OrderStatus.Paid,
            CompletionPending = true,
            PlacedAt = placedAt,
            Subtotal = subtotal,
            CouponCode = couponCode,
            Discount = discount,
            Shipping = shipping,
            TaxRate = taxRate,
            TaxJurisdiction = taxJurisdiction,
            Tax = tax,
            Total = (subtotal - discount + shipping + tax).RoundToCents(),
            ShipTo = shipTo,
            PaymentId = paymentId,
            CardBrand = cardBrand,
            CardLast4 = cardLast4,
        };
        order._lines.AddRange(lines);
        order.Raise(new OrderPlaced(order.Id, orderNumber, customerId, contactEmail, order.Total));
        return order;
    }

    public string OrderNumber { get; private set; } = "";

    /// <summary>The client's idempotency key: one order per checkout attempt.</summary>
    public Guid CheckoutId { get; private set; }

    public Guid CustomerId { get; private set; }
    public string ContactEmail { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public DateTimeOffset PlacedAt { get; private set; }
    public Money Subtotal { get; private set; }
    public string? CouponCode { get; private set; }
    public Money Discount { get; private set; }
    public Money Shipping { get; private set; }
    public decimal TaxRate { get; private set; }
    public string TaxJurisdiction { get; private set; } = "";
    public Money Tax { get; private set; }
    public Money Total { get; private set; }
    public ShipTo ShipTo { get; private set; } = null!;
    public Guid PaymentId { get; private set; }
    public string CardBrand { get; private set; } = "";
    public string CardLast4 { get; private set; } = "";
    public IReadOnlyList<OrderLine> Lines => _lines;

    public Result Ship()
    {
        var moved = Move(OrderStatus.Paid, OrderStatus.Shipped);
        if (moved.IsSuccess)
        {
            Raise(new OrderShipped(Id, OrderNumber, ContactEmail));
        }

        return moved;
    }

    public Result Deliver() => Move(OrderStatus.Shipped, OrderStatus.Delivered);
    /// <summary>
    /// Saved, but stock, coupon and payment aren't confirmed and captured yet. Saved with the order so a failure
    /// in those steps can't lose them; <see cref="Background.PendingOrderSettler"/> finishes them.
    /// </summary>
    public bool CompletionPending { get; private set; }

    public void MarkCompleted() => CompletionPending = false;

    /// <summary>When it was cancelled; null otherwise.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Cancelled, but the payment isn't refunded yet. Saved with the cancellation so a failure can't lose it.</summary>
    public bool RefundPending { get; private set; }

    /// <summary>Cancelled, but the stock isn't returned yet.</summary>
    public bool RestockPending { get; private set; }

    public Result Cancel(DateTimeOffset now)
    {
        if (CompletionPending)
        {
            return Error.Conflict("order.processing", "This order is still being processed. Please try again in a few minutes.");
        }

        var moved = Move(OrderStatus.Paid, OrderStatus.Cancelled);
        if (moved.IsSuccess)
        {
            CancelledAt = now;
            RefundPending = true;
            RestockPending = true;
        }

        return moved;
    }

    public void MarkRefunded() => RefundPending = false;
    public void MarkRestocked() => RestockPending = false;

    /// <summary>
    /// The customer deleted their account: drop who and where exactly, keep what was sold and the tax region
    /// (city, state, postal code), so totals and tax records still add up.
    /// </summary>
    public void RemovePersonalData()
    {
        ContactEmail = "";
        ShipTo = ShipTo with { RecipientName = "Deleted customer", Line1 = "Removed", Line2 = null, Phone = "" };
    }

    /// <summary>Customers may cancel themselves within an hour, while the order is still Paid (FR-035).</summary>
    public bool CanCustomerCancel(DateTimeOffset now) =>
        Status == OrderStatus.Paid && !CompletionPending && now - PlacedAt <= CustomerCancelWindow;

    private Result Move(OrderStatus from, OrderStatus to)
    {
        if (Status != from)
        {
            return Error.Conflict("order.status", $"A {Status.ToString().ToLowerInvariant()} order can't be marked {to.ToString().ToLowerInvariant()}.");
        }

        Status = to;
        return Result.Success();
    }
}

/// <summary>One line as bought: name, options, SKU and the unit price paid.</summary>
internal sealed class OrderLine : Entity
{
    private OrderLine() { }

    public OrderLine(Guid productId, Guid variantId, string productName, string sku, List<OptionValueSnapshot> optionValues, Money unitPrice, int quantity)
    {
        ProductId = productId;
        VariantId = variantId;
        ProductName = productName;
        Sku = sku;
        OptionValues = optionValues;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = (unitPrice * quantity).RoundToCents();
    }

    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public string ProductName { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public List<OptionValueSnapshot> OptionValues { get; private set; } = [];
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineTotal { get; private set; }
}
