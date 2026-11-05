using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Email;
using Ashen.BuildingBlocks.Events;

using Microsoft.Extensions.DependencyInjection;

namespace Ashen.BuildingBlocks;

public static class BuildingBlocksServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IEventBus, InProcessEventBus>();
        // One dispatcher per DbContext so a nested save inside an event handler cannot mix up events.
        services.AddTransient<IntegrationEventDispatcher>();
        services.AddScoped<IAuditLog, AuditLog>();

        services.AddValidatedOptions<StoreOptions>(StoreOptions.SectionName);
        services.AddValidatedOptions<SmtpOptions>(SmtpOptions.SectionName);
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }

    /// <summary>Binds options from configuration and fails at start-up if they are invalid (constitution IX).</summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}
