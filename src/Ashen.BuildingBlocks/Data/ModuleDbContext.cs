using Microsoft.EntityFrameworkCore;

namespace Ashen.BuildingBlocks.Data;

/// <summary>Base for ordinary module DbContexts: applies the shared conventions and the module's own configurations.</summary>
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    protected string Schema { get; } = schema;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyModuleConventions(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        modelBuilder.UseClientGeneratedIds();
    }
}
