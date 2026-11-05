using Ashen.BuildingBlocks.Data;
using Ashen.Customers.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Customers.Data;

internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "customers";

    public DbSet<CustomerProfile> Profiles => Set<CustomerProfile>();
}

internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> profile)
    {
        profile.ToTable("profiles");
        profile.HasMany(p => p.Addresses).WithOne().HasForeignKey("ProfileId").OnDelete(DeleteBehavior.Cascade);
        profile.HasMany(p => p.Wishlist).WithOne().HasForeignKey("ProfileId").OnDelete(DeleteBehavior.Cascade);
        profile.Navigation(p => p.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
        profile.Navigation(p => p.Wishlist).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> address)
    {
        address.ToTable("addresses");
        address.Property(a => a.RecipientName).HasMaxLength(100);
        address.Property(a => a.Line1).HasMaxLength(100);
        address.Property(a => a.Line2).HasMaxLength(100);
        address.Property(a => a.City).HasMaxLength(100);
        address.Property(a => a.StateCode).HasMaxLength(10);
        address.Property(a => a.PostalCode).HasMaxLength(20);
        address.Property(a => a.CountryCode).HasMaxLength(2).IsFixedLength();
        address.Property(a => a.Phone).HasMaxLength(30);
    }
}

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> item)
    {
        item.ToTable("wishlist_items");
        item.HasIndex("ProfileId", nameof(WishlistItem.ProductId)).IsUnique();
    }
}

internal sealed class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CustomersDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", CustomersDbContext.SchemaName);
        return new CustomersDbContext(options.Options);
    }
}
