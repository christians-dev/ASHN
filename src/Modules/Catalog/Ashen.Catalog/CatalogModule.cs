using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Background;
using Ashen.Catalog.Contracts;
using Ashen.Catalog.Data;
using Ashen.Catalog.Features;
using Ashen.Catalog.Features.Admin;
using Ashen.Catalog.Images;
using Ashen.Identity.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Catalog;

/// <summary>Products, variants, images, categories and stock.</summary>
public sealed class CatalogModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration, CatalogDbContext.SchemaName);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<ICatalogModule, CatalogModuleApi>();
        services.AddScoped<IModuleSeeder, CatalogSeeder>();
        services.AddSingleton<ExpiredReservationReleaser>();
        services.AddHostedService(sp => sp.GetRequiredService<ExpiredReservationReleaser>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        GetHomePage.Map(endpoints);
        ListCategories.Map(endpoints);
        BrowseProducts.Map(endpoints);
        GetProduct.Map(endpoints);

        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Catalog admin");
        ManageProducts.Map(admin);
        ManageVariants.Map(admin);
        ManageImages.Map(admin);
        ManageCategories.Map(admin);
        SetFeaturedProducts.Map(admin);
    }
}
