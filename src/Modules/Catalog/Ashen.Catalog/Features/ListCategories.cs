using Ashen.Catalog.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features;

internal static class ListCategories
{
    public sealed record CategorySummary(Guid Id, string Name, string Slug);

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/categories", Handle).AllowAnonymous().WithTags("Catalog");

    private static async Task<Ok<List<CategorySummary>>> Handle(CatalogDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await Query(db).ToListAsync(cancellationToken));

    public static IQueryable<CategorySummary> Query(CatalogDbContext db) =>
        db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategorySummary(c.Id, c.Name, c.Slug));
}
