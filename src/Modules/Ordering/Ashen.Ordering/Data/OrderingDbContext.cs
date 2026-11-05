using Ashen.BuildingBlocks.Data;
using Ashen.Ordering.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Ordering.Data;

internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "ordering";
    public const string OrderNumberSequence = "order_number_seq";

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>(OrderNumberSequence);
    }

    /// <summary>"ASH-000123": human-friendly and never reused (a database sequence, not a count).</summary>
    public async Task<string> NextOrderNumber(CancellationToken cancellationToken)
    {
        var next = await Database.SqlQueryRaw<long>($"SELECT nextval('{SchemaName}.{OrderNumberSequence}') AS \"Value\"").SingleAsync(cancellationToken);
        return $"ASH-{next:000000}";
    }
}

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> cart)
    {
        cart.ToTable("carts");
        cart.Property(c => c.AnonymousToken).HasMaxLength(64);
        cart.Property(c => c.CouponCode).HasMaxLength(30);
        cart.HasIndex(c => c.CustomerId).IsUnique().HasFilter("customer_id IS NOT NULL");
        cart.HasIndex(c => c.AnonymousToken).IsUnique().HasFilter("anonymous_token IS NOT NULL");
        cart.HasMany(c => c.Items).WithOne().HasForeignKey("CartId").OnDelete(DeleteBehavior.Cascade);
        cart.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> item)
    {
        item.ToTable("cart_items", t => t.HasCheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 99"));
        item.ComplexProperty(i => i.PriceWhenAdded).IsMoney();
        item.HasIndex("CartId", nameof(CartItem.VariantId)).IsUnique();
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> order)
    {
        order.ToTable("orders", t => t.HasCheckConstraint("ck_orders_total", "total_amount >= 0"));
        order.HasXminConcurrencyToken();
        order.Property(o => o.OrderNumber).HasMaxLength(20);
        order.Property(o => o.CouponCode).HasMaxLength(30);
        order.Property(o => o.ContactEmail).HasMaxLength(256);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        order.Property(o => o.TaxRate).HasPrecision(6, 5);
        order.Property(o => o.TaxJurisdiction).HasMaxLength(10);
        order.Property(o => o.CardBrand).HasMaxLength(20);
        order.Property(o => o.CardLast4).HasMaxLength(4).IsFixedLength();
        order.HasIndex(o => o.OrderNumber).IsUnique();
        order.HasIndex(o => o.CheckoutId).IsUnique(); // one order per checkout attempt (idempotency)
        order.HasIndex(o => new { o.CustomerId, o.PlacedAt });
        order.HasIndex(o => o.Status);
        order.ComplexProperty(o => o.Subtotal).IsMoney();
        order.ComplexProperty(o => o.Discount).IsMoney();
        order.ComplexProperty(o => o.Shipping).IsMoney();
        order.ComplexProperty(o => o.Tax).IsMoney();
        order.ComplexProperty(o => o.Total).IsMoney();
        order.ComplexProperty(o => o.ShipTo, shipTo => shipTo.ToJson());
        order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> line)
    {
        line.ToTable("order_lines", t => t.HasCheckConstraint("ck_order_lines_quantity", "quantity > 0"));
        line.Property(l => l.ProductName).HasMaxLength(120);
        line.Property(l => l.Sku).HasMaxLength(60);
        line.ComplexProperty(l => l.UnitPrice).IsMoney();
        line.ComplexProperty(l => l.LineTotal).IsMoney();
        line.ComplexCollection(l => l.OptionValues, values => values.ToJson());
    }
}

internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", OrderingDbContext.SchemaName);
        return new OrderingDbContext(options.Options);
    }
}
