using Ashen.BuildingBlocks;
using Ashen.Ordering.Domain;

namespace Ashen.UnitTests.Ordering;

public class CartTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly Money Ten = new(10m, "USD");
    private readonly Guid _tee = Guid.CreateVersion7();
    private readonly Guid _hoodie = Guid.CreateVersion7();

    [Fact]
    public void Sets_quantities_and_removes_at_zero()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), Now);

        Assert.True(cart.SetQuantity(_tee, 2, Ten, Now).IsSuccess);
        Assert.True(cart.SetQuantity(_tee, 5, Ten, Now).IsSuccess);
        Assert.Equal(5, cart.Items.Single().Quantity);

        Assert.True(cart.SetQuantity(_tee, 0, Ten, Now).IsSuccess);
        Assert.Empty(cart.Items);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Quantity_must_be_between_1_and_99(int quantity)
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), Now);

        Assert.False(cart.SetQuantity(_tee, quantity, Ten, Now).IsSuccess);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Remembers_the_price_when_first_added()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), Now);
        cart.SetQuantity(_tee, 1, Ten, Now);
        cart.SetQuantity(_tee, 2, new Money(12m, "USD"), Now);

        Assert.Equal(Ten, cart.Items.Single().PriceWhenAdded);
    }

    [Fact]
    public void Guest_cart_merges_by_adding_quantities_capped_at_99()
    {
        var customer = Cart.ForCustomer(Guid.CreateVersion7(), Now);
        customer.SetQuantity(_tee, 60, Ten, Now);
        var guest = Cart.ForGuest("token", Now);
        guest.SetQuantity(_tee, 50, Ten, Now);
        guest.SetQuantity(_hoodie, 1, Ten, Now);

        customer.MergeFrom(guest, Now);

        Assert.Equal(99, customer.Items.Single(i => i.VariantId == _tee).Quantity);
        Assert.Equal(1, customer.Items.Single(i => i.VariantId == _hoodie).Quantity);
    }
}
