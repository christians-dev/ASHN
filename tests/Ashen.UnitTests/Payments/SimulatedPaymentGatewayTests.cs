using Ashen.BuildingBlocks;
using Ashen.Payments.Gateway;

using Microsoft.Extensions.Configuration;

namespace Ashen.UnitTests.Payments;

public class SimulatedPaymentGatewayTests
{
    private static readonly Money Amount = new(25m, "USD");
    private readonly SimulatedPaymentGateway _gateway = new(new FixedClock(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero)), new ConfigurationBuilder().Build());

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CardDetails Card(string number, int month = 12, int year = 2030) => new(number, month, year, "123");

    [Theory]
    [InlineData("4242 4242 4242 4242", "Visa")]
    [InlineData("5555-5555-5555-4444", "Mastercard")]
    [InlineData("4111111111111111", "Visa")]
    public async Task Approves_good_cards(string number, string brand)
    {
        var card = await _gateway.SaveCard(Card(number));
        var payment = await _gateway.Authorize(Amount, card.Token!, "key-" + number);

        Assert.True(card.Succeeded);
        Assert.Equal(brand, card.Brand);
        Assert.Equal(number[^4..], card.Last4);
        Assert.True(payment.Succeeded);
        Assert.StartsWith("sim_pay_", payment.GatewayReference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("4000 0000 0000 0002", "CardDeclined")]
    [InlineData("4000 0000 0000 9995", "InsufficientFunds")]
    public async Task Declines_the_documented_test_cards(string number, string decline)
    {
        var card = await _gateway.SaveCard(Card(number));
        var payment = await _gateway.Authorize(Amount, card.Token!, "key-" + number);

        Assert.True(card.Succeeded); // the card is real enough to save; the payment fails
        Assert.False(payment.Succeeded);
        Assert.Equal(decline, payment.Decline.ToString());
    }

    [Theory]
    [InlineData("4242 4242 4242 4241")] // fails Luhn
    [InlineData("1234")]
    [InlineData("abcd efgh ijkl mnop")]
    public async Task Rejects_invalid_numbers(string number)
    {
        var card = await _gateway.SaveCard(Card(number));

        Assert.False(card.Succeeded);
        Assert.Equal(DeclineCode.InvalidCard, card.Decline);
    }

    [Fact]
    public async Task Rejects_expired_cards()
    {
        var card = await _gateway.SaveCard(Card("4242424242424242", month: 10, year: 2026));

        Assert.Equal(DeclineCode.ExpiredCard, card.Decline);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_result()
    {
        var card = await _gateway.SaveCard(Card("4242424242424242"));

        var first = await _gateway.Authorize(Amount, card.Token!, "same-key");
        var second = await _gateway.Authorize(Amount, card.Token!, "same-key");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Capture_void_and_refund_succeed_for_issued_references()
    {
        var card = await _gateway.SaveCard(Card("4242424242424242"));
        var payment = await _gateway.Authorize(Amount, card.Token!, "k1");

        Assert.True((await _gateway.Capture(payment.GatewayReference!, "k-capture")).Succeeded);
        Assert.True((await _gateway.Refund(payment.GatewayReference!, Amount, "k-refund")).Succeeded);
        Assert.True((await _gateway.Void(payment.GatewayReference!, "k-void")).Succeeded);
        Assert.False((await _gateway.Capture("not-ours", "k-other")).Succeeded);
    }

    [Fact]
    public async Task Token_never_contains_the_full_number()
    {
        var card = await _gateway.SaveCard(Card("4242424242424242"));

        Assert.DoesNotContain("424242424242", card.Token, StringComparison.Ordinal);
    }
}
