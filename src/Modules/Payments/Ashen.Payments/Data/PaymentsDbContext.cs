using Ashen.BuildingBlocks.Data;
using Ashen.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Payments.Data;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "payments";

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
}

internal sealed class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> card)
    {
        card.ToTable("saved_cards", t => t.HasCheckConstraint("ck_saved_cards_exp_month", "exp_month BETWEEN 1 AND 12"));
        card.Property(c => c.GatewayToken).HasMaxLength(100);
        card.Property(c => c.Brand).HasMaxLength(20);
        card.Property(c => c.Last4).HasMaxLength(4).IsFixedLength();
        card.HasIndex(c => c.CustomerId, "ix_saved_cards_customer_id");
        // At most one default card per customer.
        card.HasIndex(c => c.CustomerId, "ux_saved_cards_one_default").HasDatabaseName("ux_saved_cards_one_default").IsUnique().HasFilter("is_default");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> payment)
    {
        payment.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amount", "amount_amount >= 0"));
        payment.HasXminConcurrencyToken();
        payment.ComplexProperty(p => p.Amount).IsMoney();
        payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        payment.Property(p => p.GatewayReference).HasMaxLength(100);
        payment.Property(p => p.CardBrand).HasMaxLength(20);
        payment.Property(p => p.CardLast4).HasMaxLength(4).IsFixedLength();
        payment.Property(p => p.DeclineReason).HasMaxLength(50);
        // One payment per checkout attempt: the checkout id is the client's idempotency key.
        payment.HasIndex(p => p.CheckoutId).IsUnique();
        payment.HasIndex(p => new { p.Status, p.CreatedAt });
    }
}

internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", PaymentsDbContext.SchemaName);
        return new PaymentsDbContext(options.Options);
    }
}
