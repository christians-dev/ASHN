using Ashen.BuildingBlocks;
using Ashen.Ordering.Contracts;
using Ashen.Ordering.Domain;

namespace Ashen.UnitTests.Ordering;

public class OrderTests
{
    private static readonly DateTimeOffset PlacedAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static Money Usd(decimal amount) => new(amount, "USD");

    private static Order NewOrder()
    {
        var order = Placed();
        order.MarkCompleted(); // stock, coupon and payment confirmed
        return order;
    }

    private static Order Placed() => Order.Place(
        orderNumber: "ASH-000001",
        checkoutId: Guid.CreateVersion7(),
        customerId: Guid.CreateVersion7(),
        contactEmail: "sam@test.local",
        lines:
        [
            new OrderLine(Guid.CreateVersion7(), Guid.CreateVersion7(), "Tee", "TEE-M", [new OptionValueSnapshot("Size", "M")], Usd(20m), 2),
            new OrderLine(Guid.CreateVersion7(), Guid.CreateVersion7(), "Cap", "CAP", [], Usd(15.50m), 1),
        ],
        couponCode: "WELCOME10",
        discount: Usd(5m),
        shipping: Usd(5m),
        taxRate: 0.0625m,
        taxJurisdiction: "IL",
        tax: Usd(3.16m),
        shipTo: new ShipTo("Sam", "1 Main St", null, "Chicago", "IL", "60601", "US", "312"),
        paymentId: Guid.CreateVersion7(),
        cardBrand: "Visa",
        cardLast4: "4242",
        placedAt: PlacedAt);

    [Fact]
    public void Total_is_subtotal_minus_discount_plus_shipping_and_tax()
    {
        var order = NewOrder();

        Assert.Equal(Usd(55.50m), order.Subtotal);       // 2 × 20 + 15.50
        Assert.Equal(Usd(58.66m), order.Total);          // 55.50 − 5 + 5 + 3.16
        Assert.Equal(Usd(40m), order.Lines[0].LineTotal);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Placing_raises_order_placed()
    {
        var order = NewOrder();

        var placed = Assert.IsType<OrderPlaced>(Assert.Single(order.PendingEvents));
        Assert.Equal(("ASH-000001", "sam@test.local"), (placed.OrderNumber, placed.Email));
    }

    [Fact]
    public void Moves_through_shipped_and_delivered()
    {
        var order = NewOrder();

        Assert.False(order.Deliver().IsSuccess);   // not shipped yet
        Assert.True(order.Ship().IsSuccess);
        Assert.False(order.Cancel(DateTimeOffset.UtcNow).IsSuccess);    // too late once shipped
        Assert.True(order.Deliver().IsSuccess);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.False(order.Ship().IsSuccess);
    }

    [Fact]
    public void Order_still_completing_cannot_be_cancelled()
    {
        var order = Placed();

        Assert.True(order.CompletionPending);
        Assert.False(order.CanCustomerCancel(order.PlacedAt));
        Assert.False(order.Cancel(DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void Paid_order_can_be_cancelled_once()
    {
        var order = NewOrder();

        Assert.True(order.Cancel(DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(order.RefundPending && order.RestockPending); // saved with the cancellation, cleared once done
        Assert.False(order.Cancel(DateTimeOffset.UtcNow).IsSuccess);
        Assert.False(order.Ship().IsSuccess);
    }

    [Fact]
    public void Customer_can_cancel_only_within_an_hour_while_paid()
    {
        var order = NewOrder();

        Assert.True(order.CanCustomerCancel(PlacedAt.AddMinutes(59)));
        Assert.False(order.CanCustomerCancel(PlacedAt.AddMinutes(61)));

        order.Ship();
        Assert.False(order.CanCustomerCancel(PlacedAt.AddMinutes(1)));
    }

    [Fact]
    public void Snapshots_are_copies_not_references()
    {
        var order = NewOrder();

        Assert.Equal("Tee", order.Lines[0].ProductName);
        Assert.Equal("Chicago", order.ShipTo.City);
        Assert.Equal(0.0625m, order.TaxRate);
        Assert.Equal("4242", order.CardLast4);
    }
}
