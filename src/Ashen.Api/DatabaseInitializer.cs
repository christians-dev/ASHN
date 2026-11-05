using Ashen.BuildingBlocks.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Api;

/// <summary>
/// Applies every module's migrations, then runs module seeders, when <c>Ashen:MigrateOnStartup</c> is true
/// (on by default in Development; production migrates as a deliberate step).
/// </summary>
internal sealed partial class DatabaseInitializer(
    IServiceProvider services,
    IEnumerable<ModuleDatabaseRegistration> databases,
    IEnumerable<IModuleSeeder> seeders,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task Run(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Ashen:MigrateOnStartup"))
        {
            return;
        }

        foreach (var database in databases)
        {
            var db = (DbContext)services.GetRequiredService(database.ContextType);
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(database.ContextType.Name);
        }

        foreach (var seeder in seeders)
        {
            await seeder.Seed(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema for {Context} is up to date")]
    private partial void LogMigrated(string context);
}
