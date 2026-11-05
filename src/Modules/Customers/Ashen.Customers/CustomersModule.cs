using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Events;
using Ashen.BuildingBlocks.Web;
using Ashen.Customers.Contracts;
using Ashen.Customers.Data;
using Ashen.Customers.Features;
using Ashen.Identity.Contracts;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Customers;

/// <summary>Customer profiles: delivery addresses and wishlist.</summary>
public sealed class CustomersModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CustomersDbContext>(configuration, CustomersDbContext.SchemaName);
        services.AddScoped<ICustomersModule, CustomersModuleApi>();
        services.AddScoped<IIntegrationEventHandler<AccountDeletionRequested>, OnAccountDeletionRequested>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Addresses.Map(endpoints);
        Wishlist.Map(endpoints);
    }
}
