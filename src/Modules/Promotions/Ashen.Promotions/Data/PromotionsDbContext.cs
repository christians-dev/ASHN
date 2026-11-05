using Ashen.BuildingBlocks.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Promotions.Data;

internal sealed class PromotionsDbContext(DbContextOptions<PromotionsDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "promotions";

    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> Redemptions => Set<CouponRedemption>();
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> coupon)
    {
        coupon.ToTable("coupons", t =>
        {
            t.HasCheckConstraint("ck_coupons_value", "value > 0 AND (discount_type <> 'Percentage' OR value <= 100)");
            t.HasCheckConstraint("ck_coupons_redemption_count", "redemption_count >= 0");
        });
        coupon.HasXminConcurrencyToken();
        coupon.Property(c => c.Code).HasMaxLength(30);
        coupon.HasIndex(c => c.Code).IsUnique(); // stored upper case, so unique ignoring case
        coupon.Property(c => c.Currency).HasMaxLength(3).IsFixedLength();
        coupon.Property(c => c.DiscountType).HasConversion<string>().HasMaxLength(20);
        coupon.Property(c => c.Value).HasPrecision(12, 2);
        coupon.Property(c => c.MinimumSpendAmount).HasPrecision(12, 2);
        coupon.Ignore(c => c.MinimumSpend);
    }
}

internal sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> redemption)
    {
        redemption.ToTable("coupon_redemptions");
        redemption.HasOne<Coupon>().WithMany().HasForeignKey(r => r.CouponId).OnDelete(DeleteBehavior.Restrict);
        redemption.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        redemption.ComplexProperty(r => r.Discount).IsMoney();
        redemption.HasIndex(r => r.CheckoutId).IsUnique(); // one coupon per checkout attempt
        redemption.HasIndex(r => new { r.CouponId, r.CustomerId });
        redemption.HasIndex(r => new { r.Status, r.ExpiresAt });
    }
}

internal sealed class PromotionsDbContextFactory : IDesignTimeDbContextFactory<PromotionsDbContext>
{
    public PromotionsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PromotionsDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", PromotionsDbContext.SchemaName);
        return new PromotionsDbContext(options.Options);
    }
}
