using Ashen.Pricing.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Pricing.Features;

/// <summary>Country, currency, locale and states for formatting and address forms (FR-033).</summary>
internal static class GetRegion
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/region", Handle)
            .AllowAnonymous()
            .WithTags("Region");

    private static async Task<Ok<RegionInfo>> Handle(IPricingModule pricing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await pricing.GetActiveRegion(cancellationToken));
}
