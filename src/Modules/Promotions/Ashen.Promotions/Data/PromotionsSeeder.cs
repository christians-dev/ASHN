using Ashen.BuildingBlocks.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ashen.Promotions.Data;

/// <summary>
/// Sample coupons for local development (quickstart.md #9), only when <c>Promotions:Seed:SampleData</c> is true:
/// WELCOME10 (10% off, no limit) and an expired SUMMER25.
/// </summary>
internal sealed class PromotionsSeeder(PromotionsDbContext db, IConfiguration configuration, TimeProvider clock) : IModuleSeeder
{
    public async Task Seed(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Promotions:Seed:SampleData") || await db.Coupons.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.Coupons.Add(Coupon.Create(new CouponTerms("WELCOME10", "USD", DiscountType.Percentage, 10m, null, now.AddDays(-1), null, null, false)));
        db.Coupons.Add(Coupon.Create(new CouponTerms("SUMMER25", "USD", DiscountType.Percentage, 25m, null, now.AddMonths(-5), now.AddMonths(-2), null, false)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
