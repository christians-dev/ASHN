using System.Text.RegularExpressions;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Pricing.Contracts;
using Ashen.Promotions.Data;
using Ashen.Promotions.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Promotions.Features.Admin;

/// <summary>Coupons: list, create, edit and deactivate (FR-037).</summary>
internal static partial class ManageCoupons
{
    public sealed record CouponRequest(
        string Code,
        DiscountType DiscountType,
        decimal Value,
        decimal? MinimumSpend,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? TotalUsageLimit,
        bool OnePerCustomer);

    public sealed record CouponDto(
        Guid Id,
        string Code,
        DiscountType DiscountType,
        decimal Value,
        Money? MinimumSpend,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt,
        int? TotalUsageLimit,
        bool OnePerCustomer,
        bool IsActive,
        int RedemptionCount);

    internal sealed record Ctx(PromotionsDbContext Db, IAuditLog Audit, IPricingModule Pricing, TimeProvider Clock);

    public static void Map(RouteGroupBuilder admin)
    {
        var coupons = admin.MapGroup("/coupons");
        coupons.MapGet("", List).Produces<List<CouponDto>>();
        coupons.MapPost("", Create).Produces<CouponDto>(StatusCodes.Status201Created);
        coupons.MapPut("/{id:guid}", Update).Produces<CouponDto>();
        coupons.MapPost("/{id:guid}/deactivate", Deactivate);
    }

    private static CouponDto ToDto(Coupon c) =>
        new(c.Id, c.Code, c.DiscountType, c.Value, c.MinimumSpend, c.StartsAt, c.EndsAt, c.TotalUsageLimit, c.OnePerCustomer, c.IsActive, c.RedemptionCount);

    private static IResult NotFound() => Error.NotFound("coupon.not_found", "Coupon not found.").ToProblem();

    private static async Task<IResult> List(PromotionsDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.Coupons.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct)).Select(ToDto).ToList());

    private static async Task<IResult> Create(CouponRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        var terms = await Validate(request, c, ct);
        if (!terms.IsSuccess)
        {
            return terms.Error!.ToProblem();
        }

        if (await c.Db.Coupons.AnyAsync(x => x.Code == terms.Value.Code, ct))
        {
            return DuplicateCode();
        }

        var coupon = Coupon.Create(terms.Value);
        c.Db.Coupons.Add(coupon);
        c.Audit.Record(c.Db, "coupon.created", nameof(Coupon), coupon.Id.ToString(), request);
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/coupons/{coupon.Id}", ToDto(coupon));
    }

    private static async Task<IResult> Update(Guid id, CouponRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await c.Db.Coupons.SingleOrDefaultAsync(x => x.Id == id, ct) is not { } coupon)
        {
            return NotFound();
        }

        var terms = await Validate(request, c, ct);
        if (!terms.IsSuccess)
        {
            return terms.Error!.ToProblem();
        }

        if (await c.Db.Coupons.AnyAsync(x => x.Code == terms.Value.Code && x.Id != id, ct))
        {
            return DuplicateCode();
        }

        coupon.Update(terms.Value);
        c.Audit.Record(c.Db, "coupon.updated", nameof(Coupon), coupon.Id.ToString(), request);
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(() => TypedResults.Ok(ToDto(coupon)));
    }

    private static async Task<IResult> Deactivate(Guid id, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await c.Db.Coupons.SingleOrDefaultAsync(x => x.Id == id, ct) is not { } coupon)
        {
            return NotFound();
        }

        coupon.Deactivate();
        c.Audit.Record(c.Db, "coupon.deactivated", nameof(Coupon), coupon.Id.ToString());
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static IResult DuplicateCode() => Error.Validation("coupon.invalid", "Please fix the highlighted fields.",
        new Dictionary<string, string[]> { ["code"] = ["Another coupon already uses this code."] }).ToProblem();

    private static async Task<Result<CouponTerms>> Validate(CouponRequest r, Ctx c, CancellationToken ct)
    {
        var code = Coupon.Normalize(r.Code ?? "");
        var fields = new Dictionary<string, string[]>();
        if (!CodePattern().IsMatch(code))
        {
            fields["code"] = ["Use 3 to 30 letters, digits, - or _."];
        }

        if (r.DiscountType == DiscountType.Percentage ? r.Value is <= 0 or > 100 : r.Value is <= 0 or > 100_000)
        {
            fields["value"] = [r.DiscountType == DiscountType.Percentage ? "Enter a percentage above 0, up to 100." : "Enter an amount above 0."];
        }

        if (r.MinimumSpend is < 0 or > 100_000)
        {
            fields["minimumSpend"] = ["Enter a minimum spend of 0 or more, or leave it empty."];
        }

        var startsAt = r.StartsAt ?? c.Clock.GetUtcNow();
        if (r.EndsAt is { } ends && ends <= startsAt)
        {
            fields["endsAt"] = ["The end date must be after the start date."];
        }

        if (r.TotalUsageLimit is < 1)
        {
            fields["totalUsageLimit"] = ["Enter a limit of at least 1, or leave it empty for no limit."];
        }

        if (fields.Count > 0)
        {
            return Error.Validation("coupon.invalid", "Please fix the highlighted fields.", fields);
        }

        var currency = (await c.Pricing.GetActiveRegion(ct)).CurrencyCode;
        var value = r.DiscountType == DiscountType.FixedAmount ? Math.Round(r.Value, 2) : r.Value;
        return new CouponTerms(code, currency, r.DiscountType, value, r.MinimumSpend is { } m ? Math.Round(m, 2) : null,
            startsAt, r.EndsAt, r.TotalUsageLimit, r.OnePerCustomer);
    }

    [GeneratedRegex("^[A-Z0-9_-]{3,30}$")]
    private static partial Regex CodePattern();
}
