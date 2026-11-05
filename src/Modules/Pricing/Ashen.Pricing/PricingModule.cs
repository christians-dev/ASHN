using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Pricing.Contracts;
using Ashen.Pricing.Data;
using Ashen.Pricing.Features;
using Ashen.Pricing.Features.Admin;
using Ashen.Pricing.Tax;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Pricing;

/// <summary>Region settings, sales tax and shipping.</summary>
public sealed class PricingModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PricingDbContext>(configuration, PricingDbContext.SchemaName);
        services.AddScoped<IPricingModule, PricingModuleApi>();
        services.AddScoped<ITaxCalculator, TableTaxCalculator>();
        services.AddScoped<IModuleSeeder, PricingSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        GetRegion.Map(endpoints);

        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Pricing admin");
        ManageTaxRates.Map(admin);
        ManageStoreSettings.Map(admin);
    }
}
