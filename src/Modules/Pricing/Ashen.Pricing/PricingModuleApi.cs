using Ashen.BuildingBlocks;
using Ashen.Pricing.Contracts;
using Ashen.Pricing.Data;
using Ashen.Pricing.Tax;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Pricing;

internal sealed class PricingModuleApi(PricingDbContext db, ITaxCalculator tax) : IPricingModule
{
    public async Task<RegionInfo> GetActiveRegion(CancellationToken cancellationToken = default)
    {
        var region = await db.Regions.AsNoTracking().SingleOrDefaultAsync(r => r.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("No active region is configured; the pricing seed has not run.");

        return new RegionInfo(
            region.CountryCode,
            region.CurrencyCode,
            region.Locale,
            region.ShippingCountries,
            region.Subdivisions.Select(s => new SubdivisionInfo(s.Code, s.Name)).ToList(),
            region.PostalCodePattern);
    }

    public async Task<PriceQuote> Quote(
        Money merchandiseAfterDiscount, string countryCode, string jurisdictionCode, CancellationToken cancellationToken = default)
    {
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(cancellationToken);
        var taxed = await tax.Calculate(merchandiseAfterDiscount, countryCode, jurisdictionCode, cancellationToken);
        return new PriceQuote(settings.FlatShippingFee, taxed.Rate, taxed.Jurisdiction, taxed.Tax);
    }
}
