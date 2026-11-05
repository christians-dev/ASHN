using System.Collections.Concurrent;

using Ashen.BuildingBlocks;

using Microsoft.Extensions.Configuration;

namespace Ashen.Payments.Gateway;

internal enum DeclineCode
{
    CardDeclined,
    InsufficientFunds,
    ExpiredCard,
    InvalidCard,
}

internal sealed record CardDetails(string Number, int ExpMonth, int ExpYear, string Cvc);
internal sealed record TokenizeResult(bool Succeeded, string? Token, string? Brand, string? Last4, int ExpMonth, int ExpYear, DeclineCode? Decline);
internal sealed record GatewayResult(bool Succeeded, string? GatewayReference, DeclineCode? Decline);

/// <summary>The payment provider port (contracts/payment-gateway.md). A real provider is a new implementation.</summary>
internal interface IPaymentGateway
{
    bool IsSimulated { get; }
    Task<TokenizeResult> SaveCard(CardDetails card);
    Task<GatewayResult> Authorize(Money amount, string cardToken, string idempotencyKey);
    Task<GatewayResult> Capture(string gatewayReference);
    Task<GatewayResult> Void(string gatewayReference);
    Task<GatewayResult> Refund(string gatewayReference, Money amount);
}

/// <summary>
/// Phase 1 stand-in that moves no money (FR-025). Stateless: a token carries brand, last 4 and expiry,
/// and the outcome is decided from those alone, so saved cards behave the same after a restart.
/// </summary>
internal sealed class SimulatedPaymentGateway(TimeProvider clock, IConfiguration configuration) : IPaymentGateway
{
    private const string TokenPrefix = "sim_card_";
    private const string ReferencePrefix = "sim_pay_";

    // shortcut: idempotency answers live in memory, so they are forgotten on restart; a real provider keeps them.
    private readonly ConcurrentDictionary<string, GatewayResult> _byIdempotencyKey = new();

    public bool IsSimulated => true;

    public async Task<TokenizeResult> SaveCard(CardDetails card)
    {
        await Latency();
        var digits = new string(card.Number.Where(c => c is not (' ' or '-')).ToArray());
        if (digits.Length is < 12 or > 19 || !digits.All(char.IsAsciiDigit) || !PassesLuhn(digits)
            || card.ExpMonth is < 1 or > 12 || card.Cvc.Length is < 3 or > 4 || !card.Cvc.All(char.IsAsciiDigit))
        {
            return Failed(DeclineCode.InvalidCard);
        }

        if (IsExpired(card.ExpMonth, card.ExpYear))
        {
            return Failed(DeclineCode.ExpiredCard);
        }

        var brand = BrandOf(digits);
        var last4 = digits[^4..];
        var token = $"{TokenPrefix}{brand}_{last4}_{card.ExpMonth:00}{card.ExpYear % 100:00}_{Guid.NewGuid():N}";
        return new TokenizeResult(true, token, brand, last4, card.ExpMonth, card.ExpYear, null);
    }

    public async Task<GatewayResult> Authorize(Money amount, string cardToken, string idempotencyKey)
    {
        await Latency();
        return _byIdempotencyKey.GetOrAdd(idempotencyKey, _ => Decide(cardToken));
    }

    public Task<GatewayResult> Capture(string gatewayReference) => Acknowledge(gatewayReference);
    public Task<GatewayResult> Void(string gatewayReference) => Acknowledge(gatewayReference);
    public Task<GatewayResult> Refund(string gatewayReference, Money amount) => Acknowledge(gatewayReference);

    private GatewayResult Decide(string cardToken)
    {
        // sim_card_<brand>_<last4>_<MMYY>_<random>
        var parts = cardToken.Split('_');
        if (!cardToken.StartsWith(TokenPrefix, StringComparison.Ordinal) || parts.Length != 6 || parts[4].Length != 4)
        {
            return new GatewayResult(false, null, DeclineCode.InvalidCard);
        }

        var decline = parts[3] switch
        {
            "0002" => DeclineCode.CardDeclined,
            "9995" => DeclineCode.InsufficientFunds,
            _ when IsExpired(int.Parse(parts[4][..2]), 2000 + int.Parse(parts[4][2..])) => DeclineCode.ExpiredCard,
            _ => (DeclineCode?)null,
        };

        return decline is null
            ? new GatewayResult(true, $"{ReferencePrefix}{Guid.NewGuid():N}", null)
            : new GatewayResult(false, null, decline);
    }

    private async Task<GatewayResult> Acknowledge(string gatewayReference)
    {
        await Latency();
        return new GatewayResult(gatewayReference.StartsWith(ReferencePrefix, StringComparison.Ordinal), gatewayReference, null);
    }

    // Payments:SimulateLatencyMs makes loading states visible during local testing.
    private Task Latency() => Task.Delay(configuration.GetValue("Payments:SimulateLatencyMs", 0));

    private bool IsExpired(int month, int year)
    {
        var now = clock.GetUtcNow();
        return year < now.Year || (year == now.Year && month < now.Month);
    }

    private static TokenizeResult Failed(DeclineCode decline) => new(false, null, null, null, 0, 0, decline);

    private static string BrandOf(string digits) => digits switch
    {
        ['4', ..] => "Visa",
        ['5', >= '1' and <= '5', ..] or ['2', >= '2' and <= '7', ..] => "Mastercard",
        ['3', '4' or '7', ..] => "Amex",
        _ => "Card",
    };

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                d = d * 2 > 9 ? d * 2 - 9 : d * 2;
            }

            sum += d;
        }

        return sum % 10 == 0;
    }
}
