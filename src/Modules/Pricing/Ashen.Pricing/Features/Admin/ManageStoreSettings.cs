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

/// <summary>The flat shipping fee charged on every order; may be zero (FR-024).</summary>
internal static class ManageStoreSettings
{
    public sealed record StoreSettingsDto(Money FlatShippingFee);
    public sealed record SetStoreSettingsRequest(decimal FlatShippingFee);

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/store-settings", Get).Produces<StoreSettingsDto>();
        admin.MapPut("/store-settings", Set);
    }

    private static async Task<IResult> Get(PricingDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new StoreSettingsDto((await db.StoreSettings.AsNoTracking().SingleAsync(ct)).FlatShippingFee));

    private static async Task<IResult> Set(SetStoreSettingsRequest request, PricingDbContext db, IAuditLog audit, CancellationToken ct)
    {
        if (request.FlatShippingFee is < 0 or > 1000)
        {
            return Error.Validation("store_settings.invalid", "Please fix the highlighted fields.",
                new Dictionary<string, string[]> { ["flatShippingFee"] = ["Enter a shipping fee from 0 to 1,000."] }).ToProblem();
        }

        var settings = await db.StoreSettings.SingleAsync(ct);
        var currency = (await db.Regions.AsNoTracking().SingleAsync(r => r.IsActive, ct)).CurrencyCode;
        settings.ChangeShippingFee(new Money(request.FlatShippingFee, currency).RoundToCents());

        audit.Record(db, "store_settings.shipping_fee_set", nameof(StoreSettings), settings.Id.ToString(), new { request.FlatShippingFee });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
