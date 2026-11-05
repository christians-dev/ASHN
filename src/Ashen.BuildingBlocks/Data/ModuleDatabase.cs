using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.BuildingBlocks.Data;

/// <summary>Records a module DbContext so start-up can migrate every module's schema.</summary>
public sealed record ModuleDatabaseRegistration(Type ContextType);

/// <summary>Inserts a module's reference or sample data after migrations; must be safe to run repeatedly.</summary>
public interface IModuleSeeder
{
    Task Seed(CancellationToken cancellationToken);
}

public static class ModuleDatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddModuleDatabase<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddSingleton(new ModuleDatabaseRegistration(typeof(TContext)));
        return services;
    }
}
