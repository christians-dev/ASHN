using Ashen.BuildingBlocks;

namespace Ashen.Pricing.Contracts;

/// <summary>Region, tax and shipping rules other modules may use (module-contracts.md).</summary>
public interface IPricingModule
{
    Task<RegionInfo> GetActiveRegion(CancellationToken cancellationToken = default);

    /// <summary>Shipping and sales tax for merchandise (already discounted) delivered to a state (FR-024, FR-030).</summary>
    Task<PriceQuote> Quote(Money merchandiseAfterDiscount, string countryCode, string jurisdictionCode, CancellationToken cancellationToken = default);
}

/// <summary>Shipping is never taxed; <see cref="Tax"/> is already rounded to the cent.</summary>
public sealed record PriceQuote(Money Shipping, decimal TaxRate, string TaxJurisdiction, Money Tax);

public sealed record RegionInfo(
    string CountryCode,
    string CurrencyCode,
    string Locale,
    IReadOnlyList<string> ShippingCountries,
    IReadOnlyList<SubdivisionInfo> States,
    string PostalCodePattern);

public sealed record SubdivisionInfo(string Code, string Name);
