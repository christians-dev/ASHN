using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Events;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Background;
using Ashen.Ordering.Checkout;
using Ashen.Ordering.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Features;
using Ashen.Ordering.Features.Admin;
using Ashen.Ordering.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Ordering;

/// <summary>Cart, checkout and orders.</summary>
public sealed class OrderingModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrderingDbContext>(configuration, OrderingDbContext.SchemaName);
        services.AddScoped<CartResolver>();
        services.AddScoped<CheckoutPricing>();
        services.AddScoped<CheckoutOrchestrator>();
        services.AddScoped<OrderCancellation>();
        services.AddScoped<OrderCompletion>();
        services.AddSingleton<PendingOrderSettler>();
        services.AddHostedService(sp => sp.GetRequiredService<PendingOrderSettler>());
        services.AddScoped<IOrderingModule, OrderingModuleApi>();
        services.AddScoped<IIntegrationEventHandler<OrderPlaced>, OnOrderPlaced>();
        services.AddScoped<IIntegrationEventHandler<OrderShipped>, OnOrderShipped>();
        services.AddScoped<IIntegrationEventHandler<AccountDeletionRequested>, OnAccountDeletionRequested>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CartEndpoints.Map(endpoints);
        CheckoutEndpoints.Map(endpoints);
        MyOrders.Map(endpoints);

        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Orders admin");
        ManageOrders.Map(admin);
    }
}
