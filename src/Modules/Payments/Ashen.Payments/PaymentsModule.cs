using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Events;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Payments.Background;
using Ashen.Payments.Contracts;
using Ashen.Payments.Data;
using Ashen.Payments.Features;
using Ashen.Payments.Gateway;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Payments;

internal sealed record PaymentModeResponse(bool Simulated);

/// <summary>Payments through a gateway port; phase 1 uses the simulated gateway only (FR-025).</summary>
public sealed class PaymentsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration, PaymentsDbContext.SchemaName);
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();
        services.AddScoped<IPaymentsModule, PaymentsModuleApi>();
        services.AddSingleton<StaleAuthorizationVoider>();
        services.AddHostedService(sp => sp.GetRequiredService<StaleAuthorizationVoider>());
        services.AddScoped<IIntegrationEventHandler<AccountDeletionRequested>, OnAccountDeletionRequested>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Lets the storefront say plainly that no real money is taken (FR-028).
        endpoints.MapGet("/payments/mode", (IPaymentGateway gateway) => TypedResults.Ok(new PaymentModeResponse(gateway.IsSimulated)))
            .AllowAnonymous()
            .WithTags("Payments");

        SavedCards.Map(endpoints);
    }
}
