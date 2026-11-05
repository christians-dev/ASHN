using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Promotions.Background;
using Ashen.Promotions.Contracts;
using Ashen.Promotions.Data;
using Ashen.Promotions.Features.Admin;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Promotions;

/// <summary>Discount coupons (FR-037, FR-038).</summary>
public sealed class PromotionsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PromotionsDbContext>(configuration, PromotionsDbContext.SchemaName);
        services.AddScoped<IPromotionsModule, PromotionsModuleApi>();
        services.AddScoped<IModuleSeeder, PromotionsSeeder>();
        services.AddSingleton<ExpiredRedemptionReleaser>();
        services.AddHostedService(sp => sp.GetRequiredService<ExpiredRedemptionReleaser>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Promotions admin");
        ManageCoupons.Map(admin);
    }
}
