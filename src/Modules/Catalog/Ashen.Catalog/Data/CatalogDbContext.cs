using Ashen.BuildingBlocks.Data;
using Ashen.Catalog.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Catalog.Data;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "catalog";

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Variant> Variants => Set<Variant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trigram index for text search (research R6); created in the migration.
        modelBuilder.HasPostgresExtension("pg_trgm");
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> category)
    {
        category.ToTable("categories");
        category.Property(c => c.Name).HasMaxLength(60);
        category.Property(c => c.Slug).HasMaxLength(80);
        category.HasIndex(c => c.Name).IsUnique();
        category.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> product)
    {
        product.ToTable("products");
        product.HasXminConcurrencyToken();
        product.Property(p => p.Name).HasMaxLength(120);
        product.Property(p => p.Slug).HasMaxLength(140);
        product.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        product.HasIndex(p => p.Slug).IsUnique();
        product.HasIndex(p => new { p.Status, p.CategoryId });
        product.ComplexCollection(p => p.Options, options => options.ToJson());

        product.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        product.HasMany(p => p.Variants).WithOne().HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade);
        product.HasMany(p => p.Images).WithOne().HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade);
        product.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        product.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> variant)
    {
        variant.ToTable("variants", t =>
        {
            t.HasCheckConstraint("ck_variants_stock_on_hand", "stock_on_hand >= 0");
            t.HasCheckConstraint("ck_variants_stock_reserved", "stock_reserved >= 0");
            t.HasCheckConstraint("ck_variants_price", "price_amount >= 0");
        });
        variant.HasXminConcurrencyToken();
        variant.Property(v => v.Sku).HasMaxLength(60);
        variant.Property(v => v.OptionKey).HasMaxLength(300);
        variant.HasIndex(v => v.Sku).IsUnique();
        variant.HasIndex("ProductId", nameof(Variant.OptionKey)).IsUnique();
        variant.ComplexProperty(v => v.Price).IsMoney();
        variant.ComplexCollection(v => v.OptionValues, values => values.ToJson());
        variant.Ignore(v => v.Available);
        variant.Ignore(v => v.IsAvailable);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> reservation)
    {
        reservation.ToTable("stock_reservations", t => t.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0"));
        reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        reservation.HasIndex(r => r.CheckoutId);
        reservation.HasIndex(r => new { r.Status, r.ExpiresAt });
        reservation.HasOne<Variant>().WithMany().HasForeignKey(r => r.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> image)
    {
        image.ToTable("product_images");
        image.Property(i => i.StoragePath).HasMaxLength(300);
        image.Property(i => i.AltText).HasMaxLength(200);
    }
}

internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", CatalogDbContext.SchemaName);
        return new CatalogDbContext(options.Options);
    }
}
