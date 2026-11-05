using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Contracts;
using Ashen.Reviews.Contracts;
using Ashen.Reviews.Data;
using Ashen.Reviews.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Reviews.Features;

/// <summary>
/// A product's visible reviews (FR-040), and the signed-in customer's own review, which only a verified buyer
/// (a Delivered order containing the product) may write (FR-039).
/// </summary>
internal static class ProductReviews
{
    public sealed record ReviewDto(Guid Id, string Author, int Rating, string? Text, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    /// <param name="Mine">The viewer's own review, shown to them even when an admin has hidden it.</param>
    /// <param name="CanReview">Whether the viewer may write or edit a review (signed in and a verified buyer).</param>
    public sealed record ProductReviewsDto(RatingSummary? Summary, List<ReviewDto> Reviews, ReviewDto? Mine, bool MineIsHidden, bool CanReview);

    public sealed record ReviewRequest(int Rating, string? Text);

    internal sealed record Ctx(ReviewsDbContext Db, ICurrentUser User, IOrderingModule Ordering, IReviewsModule Ratings, TimeProvider Clock);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var reviews = endpoints.MapGroup("/products/{productId:guid}/reviews").WithTags("Reviews");
        reviews.MapGet("", List).AllowAnonymous().Produces<ProductReviewsDto>();
        reviews.MapPut("/mine", Save).Produces<ReviewDto>();
        reviews.MapDelete("/mine", Delete);
    }

    private static ReviewDto ToDto(Review r) => new(r.Id, r.AuthorDisplayName, r.Rating, r.Text, r.CreatedAt, r.UpdatedAt);

    // shortcut: every visible review in one response; page it when products gather hundreds of reviews.
    private static async Task<IResult> List(Guid productId, [AsParameters] Ctx c, CancellationToken ct)
    {
        var visible = await c.Db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && !r.IsHidden)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var summary = (await c.Ratings.GetRatings([productId], ct)).GetValueOrDefault(productId);

        Review? mine = null;
        var canReview = false;
        if (c.User.UserId is { } customerId)
        {
            mine = await c.Db.Reviews.AsNoTracking().SingleOrDefaultAsync(r => r.ProductId == productId && r.CustomerId == customerId, ct);
            canReview = await c.Ordering.HasDeliveredProduct(customerId, productId, ct);
        }

        return TypedResults.Ok(new ProductReviewsDto(
            summary,
            visible.Where(r => r.Id != mine?.Id).Select(ToDto).ToList(),
            mine is null ? null : ToDto(mine),
            mine?.IsHidden ?? false,
            canReview));
    }

    private static async Task<IResult> Save(Guid productId, ReviewRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        var customerId = c.User.UserId!.Value;
        if (!await c.Ordering.HasDeliveredProduct(customerId, productId, ct))
        {
            return Error.Forbidden("review.not_verified", "Only customers who received this product can review it.").ToProblem();
        }

        var now = c.Clock.GetUtcNow();
        var review = await c.Db.Reviews.SingleOrDefaultAsync(r => r.ProductId == productId && r.CustomerId == customerId, ct);
        if (review is null)
        {
            var written = Review.Write(productId, customerId, Review.PublicName(c.User.DisplayName), request.Rating, request.Text, now);
            if (!written.IsSuccess)
            {
                return written.Error!.ToProblem();
            }

            review = written.Value;
            c.Db.Reviews.Add(review);
        }
        else if (review.Edit(request.Rating, request.Text, now) is { IsSuccess: false } invalid)
        {
            return invalid.Error!.ToProblem();
        }

        try
        {
            return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(() => TypedResults.Ok(ToDto(review)));
        }
        catch (DbUpdateException)
        {
            // Two first submits at once: the unique (product, customer) index kept just one.
            return Error.Conflict("review.duplicate", "Your review was just saved. Reload to edit it.").ToProblem();
        }
    }

    private static async Task<IResult> Delete(Guid productId, [AsParameters] Ctx c, CancellationToken ct)
    {
        var deleted = await c.Db.Reviews.Where(r => r.ProductId == productId && r.CustomerId == c.User.UserId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Error.NotFound("review.not_found", "You haven't reviewed this product.").ToProblem() : TypedResults.NoContent();
    }
}
