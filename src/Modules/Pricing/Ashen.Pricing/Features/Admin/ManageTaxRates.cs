using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Web;
using Ashen.Pricing.Data;
using Ashen.Pricing.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Pricing.Features.Admin;

/// <summary>
/// Sales-tax rates per state (FR-030, SC-011). A change adds a dated row rather than overwriting, so a rate can be
/// scheduled ahead and orders already placed keep the rate they were charged.
/// </summary>
internal static class ManageTaxRates
{
    public const decimal MaxRate = 0.2m;

    public sealed record TaxRateDto(string JurisdictionCode, string JurisdictionName, decimal Rate, DateTimeOffset EffectiveFrom, bool IsCurrent, bool IsScheduled);
    public sealed record SetTaxRateRequest(decimal Rate, DateTimeOffset? EffectiveFrom);

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/tax-rates", List).Produces<List<TaxRateDto>>();
        admin.MapPut("/tax-rates/{jurisdictionCode}", Set);
    }

    private static async Task<IResult> List(PricingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var region = await db.Regions.AsNoTracking().SingleAsync(r => r.IsActive, ct);
        var rates = await db.TaxRates.AsNoTracking().Where(r => r.RegionId == region.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var current = rates.Where(r => r.EffectiveFrom <= now)
            .GroupBy(r => r.JurisdictionCode)
            .Select(g => g.MaxBy(r => r.EffectiveFrom)!.Id)
            .ToHashSet();

        return TypedResults.Ok(rates
            .OrderBy(r => r.JurisdictionCode).ThenByDescending(r => r.EffectiveFrom)
            .Select(r => new TaxRateDto(
                r.JurisdictionCode,
                region.Subdivisions.FirstOrDefault(s => s.Code == r.JurisdictionCode)?.Name ?? r.JurisdictionCode,
                r.Rate,
                r.EffectiveFrom,
                current.Contains(r.Id),
                r.EffectiveFrom > now))
            .ToList());
    }

    private static async Task<IResult> Set(
        string jurisdictionCode, SetTaxRateRequest request, PricingDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var region = await db.Regions.SingleAsync(r => r.IsActive, ct);
        var code = jurisdictionCode.Trim().ToUpperInvariant();
        var fields = new Dictionary<string, string[]>();
        if (region.Subdivisions.All(s => s.Code != code))
        {
            fields["jurisdictionCode"] = ["Choose one of the store's states."];
        }

        if (request.Rate is < 0 or >= MaxRate)
        {
            fields["rate"] = [$"Enter a rate from 0% up to (not including) {MaxRate:P0}."];
        }

        if (fields.Count > 0)
        {
            return Error.Validation("tax_rate.invalid", "Please fix the highlighted fields.", fields).ToProblem();
        }

        var effectiveFrom = request.EffectiveFrom ?? clock.GetUtcNow();
        var existing = await db.TaxRates.SingleOrDefaultAsync(
            r => r.RegionId == region.Id && r.JurisdictionCode == code && r.EffectiveFrom == effectiveFrom, ct);
        if (existing is null)
        {
            db.TaxRates.Add(new TaxRate(region.Id, code, request.Rate, effectiveFrom));
        }
        else
        {
            existing.ChangeRate(request.Rate);
        }

        audit.Record(db, "tax_rate.set", nameof(TaxRate), code, new { request.Rate, effectiveFrom });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
