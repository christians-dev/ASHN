using Ashen.BuildingBlocks;
using Ashen.Pricing.Data;
using Ashen.Pricing.Domain;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Pricing.Tax;

internal sealed record TaxResult(decimal Rate, string Jurisdiction, Money Tax);

/// <summary>Swappable tax port (constitution III): city/county rates or an external tax service replace the table.</summary>
internal interface ITaxCalculator
{
    Task<TaxResult> Calculate(Money taxableAmount, string countryCode, string jurisdictionCode, CancellationToken cancellationToken);
}

/// <summary>One rate per state from the admin's table (FR-030, FR-031); states without a rate pay no tax.</summary>
internal sealed class TableTaxCalculator(PricingDbContext db, TimeProvider clock) : ITaxCalculator
{
    public async Task<TaxResult> Calculate(Money taxableAmount, string countryCode, string jurisdictionCode, CancellationToken cancellationToken)
    {
        var rates = await db.TaxRates.AsNoTracking()
            .Where(r => r.JurisdictionCode == jurisdictionCode
                && db.Regions.Any(region => region.Id == r.RegionId && region.IsActive && region.CountryCode == countryCode))
            .ToListAsync(cancellationToken);

        return Calculate(taxableAmount, jurisdictionCode, rates, clock.GetUtcNow());
    }

    /// <summary>The latest rate already in effect, applied and rounded half-up to the cent.</summary>
    public static TaxResult Calculate(Money taxableAmount, string jurisdictionCode, IEnumerable<TaxRate> rates, DateTimeOffset now)
    {
        var rate = rates
            .Where(r => r.JurisdictionCode == jurisdictionCode && r.EffectiveFrom <= now)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => r.Rate)
            .FirstOrDefault();

        return new TaxResult(rate, jurisdictionCode, (taxableAmount * rate).RoundToCents());
    }
}
