using Ashen.BuildingBlocks.Data;
using Ashen.Pricing.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Pricing.Data;

internal sealed class PricingDbContext(DbContextOptions<PricingDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "pricing";

    public DbSet<RegionSetting> Regions => Set<RegionSetting>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
}

internal sealed class RegionSettingConfiguration : IEntityTypeConfiguration<RegionSetting>
{
    public void Configure(EntityTypeBuilder<RegionSetting> region)
    {
        region.ToTable("region_settings");
        region.Property(r => r.CountryCode).HasMaxLength(2).IsFixedLength();
        region.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength();
        region.Property(r => r.Locale).HasMaxLength(20);
        region.Property(r => r.PostalCodePattern).HasMaxLength(200);
        region.ComplexCollection(r => r.Subdivisions, s => s.ToJson());
        // At most one active region.
        region.HasIndex(r => r.IsActive).IsUnique().HasFilter("is_active");
    }
}

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> tax)
    {
        tax.ToTable("tax_rates", t => t.HasCheckConstraint("ck_tax_rates_rate", "rate >= 0 AND rate < 1"));
        tax.Property(t => t.JurisdictionCode).HasMaxLength(10);
        tax.Property(t => t.Rate).HasPrecision(6, 5);
        tax.HasIndex(t => new { t.RegionId, t.JurisdictionCode, t.EffectiveFrom }).IsUnique();
    }
}

internal sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    public void Configure(EntityTypeBuilder<StoreSettings> settings)
    {
        settings.ToTable("store_settings", t =>
            t.HasCheckConstraint("ck_store_settings_shipping", "flat_shipping_fee_amount >= 0"));
        settings.ComplexProperty(s => s.FlatShippingFee).IsMoney();
    }
}

internal sealed class PricingDbContextFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    public PricingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PricingDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", PricingDbContext.SchemaName);
        return new PricingDbContext(options.Options);
    }
}
