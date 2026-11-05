using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Contracts;
using Ashen.Reviews.Data;
using Ashen.Reviews.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Reviews.Features.Admin;

/// <summary>All reviews, newest first, optionally only hidden or visible ones; hide and unhide (FR-040).</summary>
internal static class ModerateReviews
{
    public sealed record ReviewsQuery(
        [FromQuery(Name = "hidden")] bool? Hidden,
        [FromQuery(Name = "page")] int Page = 1,
        [FromQuery(Name = "pageSize")] int PageSize = 24);

    public sealed record AdminReviewDto(
        Guid Id, Guid ProductId, string ProductName, string? ProductSlug, string Author, int Rating, string? Text, bool IsHidden, DateTimeOffset CreatedAt);

    public static void Map(RouteGroupBuilder admin)
    {
        var reviews = admin.MapGroup("/reviews");
        reviews.MapGet("", List).Produces<PagedResult<AdminReviewDto>>();
        reviews.MapPost("/{id:guid}/hide", (Guid id, ReviewsDbContext db, IAuditLog audit, CancellationToken ct) =>
            Change(id, r => r.Hide(), "review.hidden", db, audit, ct));
        reviews.MapPost("/{id:guid}/unhide", (Guid id, ReviewsDbContext db, IAuditLog audit, CancellationToken ct) =>
            Change(id, r => r.Unhide(), "review.unhidden", db, audit, ct));
    }

    private static async Task<IResult> List([AsParameters] ReviewsQuery query, ReviewsDbContext db, ICatalogModule catalog, CancellationToken ct)
    {
        var paging = new PagedRequest(query.Page, query.PageSize);
        var reviews = db.Reviews.AsNoTracking().Where(r => query.Hidden == null || r.IsHidden == query.Hidden);
        var total = await reviews.CountAsync(ct);
        var page = await reviews.OrderByDescending(r => r.CreatedAt).Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);
        var products = (await catalog.GetProductSummaries([.. page.Select(r => r.ProductId).Distinct()], ct)).ToDictionary(p => p.ProductId);

        var items = page.Select(r => new AdminReviewDto(
            r.Id,
            r.ProductId,
            products.GetValueOrDefault(r.ProductId)?.Name ?? "(deleted product)",
            products.GetValueOrDefault(r.ProductId)?.Slug,
            r.AuthorDisplayName,
            r.Rating,
            r.Text,
            r.IsHidden,
            r.CreatedAt)).ToList();
        return TypedResults.Ok(new PagedResult<AdminReviewDto>(items, paging.SafePage, paging.SafePageSize, total));
    }

    private static async Task<IResult> Change(Guid id, Action<Review> change, string action, ReviewsDbContext db, IAuditLog audit, CancellationToken ct)
    {
        if (await db.Reviews.SingleOrDefaultAsync(r => r.Id == id, ct) is not { } review)
        {
            return Error.NotFound("review.not_found", "Review not found.").ToProblem();
        }

        change(review);
        audit.Record(db, action, nameof(Review), review.Id.ToString());
        return (await db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }
}
