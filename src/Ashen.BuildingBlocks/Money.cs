using System.Text.Json.Serialization;

namespace Ashen.BuildingBlocks;

/// <summary>An amount of money in one ISO 4217 currency. Never use floating point for money (constitution V).</summary>
public readonly record struct Money(decimal Amount, string Currency) : IComparable<Money>
{
    public string Currency { get; } = IsValidCurrency(Currency)
        ? Currency
        : throw new ArgumentException("Currency must be a 3-letter upper-case ISO 4217 code.", nameof(Currency));

    public static Money Zero(string currency) => new(0m, currency);

    [JsonIgnore]
    public bool IsZero => Amount == 0m;

    [JsonIgnore]
    public bool IsNegative => Amount < 0m;

    /// <summary>Rounds to whole cents, half away from zero (0.005 → 0.01), as US totals expect.</summary>
    public Money RoundToCents() => new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero), Currency);

    public static Money Min(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount <= b.Amount ? a : b;
    }

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);
    public static Money operator *(Money a, int quantity) => new(a.Amount * quantity, a.Currency);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
        {
            throw new InvalidOperationException($"Cannot combine {a.Currency} and {b.Currency}.");
        }
    }

    private static bool IsValidCurrency(string? currency) =>
        currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper);
}
