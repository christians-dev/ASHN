using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.BuildingBlocks.Data;

public static class DbContextOptionsBuilderExtensions
{
    /// <summary>The connection string name Aspire provides for the store's database.</summary>
    public const string ConnectionStringName = "ashen";

    /// <summary>PostgreSQL, snake_case names, and the migrations history table inside the module's schema.</summary>
    public static DbContextOptionsBuilder UseModuleDefaults(
        this DbContextOptionsBuilder options, string connectionString, string schema) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

    /// <summary>Registers a module DbContext with the shared defaults and the integration event dispatcher.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<TContext>((sp, options) => options
            .UseModuleDefaults(connectionString, schema)
            .AddInterceptors(sp.GetRequiredService<IntegrationEventDispatcher>()));

        return services.AddModuleDatabase<TContext>();
    }
}
