using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using static Ashen.Ordering.Shared.OrderViews;

namespace Ashen.Ordering.Features;

/// <summary>A customer's own orders, and cancelling within the hour (FR-035). Other customers' orders are 404.</summary>
internal static class MyOrders
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/me/orders").WithTags("Orders");
        orders.MapGet("", List).Produces<List<OrderSummaryDto>>();
        orders.MapGet("/{id:guid}", Get).Produces<OrderDetailDto>();
        orders.MapPost("/{id:guid}/cancel", Cancel);
    }

    private static Error NotFound => Error.NotFound("order.not_found", "Order not found.");

    private static IQueryable<Order> Mine(OrderingDbContext db, ICurrentUser user) =>
        db.Orders.Include(o => o.Lines).Where(o => o.CustomerId == user.UserId);

    // shortcut: no paging, a customer has few orders; page like the admin list if that changes.
    private static async Task<IResult> List(OrderingDbContext db, ICurrentUser user, CancellationToken ct) =>
        TypedResults.Ok((await Mine(db, user).AsNoTracking().OrderByDescending(o => o.PlacedAt).ToListAsync(ct)).Select(o => o.ToSummary()).ToList());

    private static async Task<IResult> Get(Guid id, OrderingDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        await Mine(db, user).AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) is { } order
            ? TypedResults.Ok(order.ToDetail(order.CanCustomerCancel(clock.GetUtcNow())))
            : NotFound.ToProblem();

    private static async Task<IResult> Cancel(
        Guid id, OrderingDbContext db, ICurrentUser user, OrderCancellation cancellation, TimeProvider clock, CancellationToken ct)
    {
        if (await Mine(db, user).SingleOrDefaultAsync(o => o.Id == id, ct) is not { } order)
        {
            return NotFound.ToProblem();
        }

        if (!order.CanCustomerCancel(clock.GetUtcNow()))
        {
            return Error.Conflict(
                "order.cannot_cancel",
                order.Status == OrderStatus.Paid
                    ? "Orders can only be cancelled within an hour of placing them. Please contact us."
                    : $"This order is already {order.Status.ToString().ToLowerInvariant()} and can't be cancelled.").ToProblem();
        }

        return (await cancellation.Cancel(order, byAdmin: false, ct)).ToHttp(TypedResults.NoContent);
    }
}
