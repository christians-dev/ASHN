using Ashen.BuildingBlocks.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.BuildingBlocks.Data;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Shared conventions for every module's model: its own schema and its own audit table.
    /// Works for any DbContext, including ones that must inherit a framework base class (Identity).
    /// </summary>
    public static ModelBuilder ApplyModuleConventions(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.HasDefaultSchema(schema);

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("audit_entries");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).HasMaxLength(100);
            audit.Property(a => a.EntityType).HasMaxLength(100);
            audit.Property(a => a.EntityId).HasMaxLength(100);
            audit.Property(a => a.Details).HasColumnType("jsonb");
            audit.HasIndex(a => new { a.EntityType, a.EntityId });
        });

        return modelBuilder;
    }

    /// <summary>
    /// Our entities create their own ids (<see cref="Domain.Entity"/>), so EF must not treat a new child with an id
    /// as an existing row: without this, a variant added to a loaded product is saved as an UPDATE of nothing.
    /// </summary>
    public static ModelBuilder UseClientGeneratedIds(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Domain.Entity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Domain.Entity.Id)).ValueGeneratedNever();
        }

        return modelBuilder;
    }

    /// <summary>Maps a <see cref="Money"/> property to <c>*_amount numeric(12,2)</c> + <c>*_currency char(3)</c>.</summary>
    public static ComplexPropertyBuilder<Money> IsMoney(this ComplexPropertyBuilder<Money> money)
    {
        money.Property(m => m.Amount).HasPrecision(12, 2);
        money.Property(m => m.Currency).HasMaxLength(3).IsFixedLength();
        return money;
    }

    /// <summary>Uses PostgreSQL's <c>xmin</c> row version as an optimistic concurrency token (constitution VIII).</summary>
    public static EntityTypeBuilder<T> HasXminConcurrencyToken<T>(this EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<uint>("Version").IsRowVersion();
        return entity;
    }
}
