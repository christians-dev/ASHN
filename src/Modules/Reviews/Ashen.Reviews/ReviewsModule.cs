using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Events;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Reviews.Contracts;
using Ashen.Reviews.Data;
using Ashen.Reviews.Features;
using Ashen.Reviews.Features.Admin;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.Reviews;

/// <summary>Product reviews from verified buyers, with admin moderation (FR-039, FR-040).</summary>
public sealed class ReviewsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ReviewsDbContext>(configuration, ReviewsDbContext.SchemaName);
        services.AddScoped<IReviewsModule, ReviewsModuleApi>();
        services.AddScoped<IIntegrationEventHandler<AccountDeletionRequested>, OnAccountDeletionRequested>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProductReviews.Map(endpoints);

        var admin = endpoints.MapGroup("/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Reviews admin");
        ModerateReviews.Map(admin);
    }
}
