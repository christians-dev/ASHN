using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Pricing.Domain;

/// <summary>Where the store sells and how it formats things. Exactly one is active (FR-033).</summary>
internal sealed class RegionSetting : Entity
{
    private RegionSetting() { }

    public RegionSetting(string countryCode, string currencyCode, string locale, IEnumerable<string> shippingCountries, IEnumerable<Subdivision> subdivisions, string postalCodePattern)
    {
        CountryCode = countryCode;
        CurrencyCode = currencyCode;
        Locale = locale;
        ShippingCountries = shippingCountries.ToList();
        Subdivisions = subdivisions.ToList();
        PostalCodePattern = postalCodePattern;
        IsActive = true;
    }

    public string CountryCode { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public string Locale { get; private set; } = "";
    public List<string> ShippingCountries { get; private set; } = [];

    /// <summary>States/provinces addresses may use, and tax jurisdictions are keyed by.</summary>
    public List<Subdivision> Subdivisions { get; private set; } = [];

    /// <summary>Regular expression a postal code in this region must match, e.g. US ZIP codes.</summary>
    public string PostalCodePattern { get; private set; } = "";

    public bool IsActive { get; private set; }
}

internal sealed record Subdivision(string Code, string Name);

/// <summary>A sales-tax rate for one jurisdiction from a date; the latest effective rate wins (FR-030).</summary>
internal sealed class TaxRate : Entity
{
    private TaxRate() { }

    public TaxRate(Guid regionId, string jurisdictionCode, decimal rate, DateTimeOffset effectiveFrom)
    {
        RegionId = regionId;
        JurisdictionCode = jurisdictionCode;
        Rate = rate;
        EffectiveFrom = effectiveFrom;
    }

    public Guid RegionId { get; private set; }
    public string JurisdictionCode { get; private set; } = "";
    public decimal Rate { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }

    public void ChangeRate(decimal rate) => Rate = rate;
}

/// <summary>Store-wide pricing settings (FR-024).</summary>
internal sealed class StoreSettings : Entity
{
    private StoreSettings() { }

    public StoreSettings(Money flatShippingFee) => FlatShippingFee = flatShippingFee;

    public Money FlatShippingFee { get; private set; }

    public void ChangeShippingFee(Money fee) => FlatShippingFee = fee;
}
