using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;
using Ashen.Ordering.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using static Ashen.Ordering.Shared.OrderViews;

namespace Ashen.Ordering.Features.Admin;

/// <summary>All orders, filtered by status, and moving them Paid → Shipped → Delivered or Cancelled (FR-036).</summary>
internal static class ManageOrders
{
    public sealed record OrdersQuery(
        [FromQuery(Name = "status")] OrderStatus? Status,
        [FromQuery(Name = "page")] int Page = 1,
        [FromQuery(Name = "pageSize")] int PageSize = 24);

    public static void Map(RouteGroupBuilder admin)
    {
        var orders = admin.MapGroup("/orders");
        orders.MapGet("", List).Produces<PagedResult<OrderSummaryDto>>();
        orders.MapGet("/{id:guid}", Get).Produces<OrderDetailDto>();
        orders.MapPost("/{id:guid}/ship", (Guid id, OrderingDbContext db, IAuditLog audit, CancellationToken ct) =>
            Move(id, o => o.Ship(), "order.shipped", db, audit, ct));
        orders.MapPost("/{id:guid}/deliver", (Guid id, OrderingDbContext db, IAuditLog audit, CancellationToken ct) =>
            Move(id, o => o.Deliver(), "order.delivered", db, audit, ct));
        orders.MapPost("/{id:guid}/cancel", Cancel);
    }

    private static Error NotFound => Error.NotFound("order.not_found", "Order not found.");

    private static async Task<IResult> List([AsParameters] OrdersQuery query, OrderingDbContext db, CancellationToken ct)
    {
        var paging = new PagedRequest(query.Page, query.PageSize);
        var orders = db.Orders.AsNoTracking().Where(o => query.Status == null || o.Status == query.Status);
        var total = await orders.CountAsync(ct);
        var items = await orders.Include(o => o.Lines)
            .OrderByDescending(o => o.PlacedAt)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<OrderSummaryDto>([.. items.Select(o => o.ToSummary())], paging.SafePage, paging.SafePageSize, total));
    }

    private static async Task<IResult> Get(Guid id, OrderingDbContext db, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct) is { } order
            ? TypedResults.Ok(order.ToDetail(canCancel: order.Status == OrderStatus.Paid))
            : NotFound.ToProblem();

    private static async Task<IResult> Move(
        Guid id, Func<Order, Result> transition, string action, OrderingDbContext db, IAuditLog audit, CancellationToken ct)
    {
        if (await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct) is not { } order)
        {
            return NotFound.ToProblem();
        }

        var moved = transition(order);
        if (!moved.IsSuccess)
        {
            return moved.ToHttp(TypedResults.NoContent);
        }

        audit.Record(db, action, nameof(Order), order.Id.ToString());
        return (await db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static async Task<IResult> Cancel(Guid id, OrderingDbContext db, OrderCancellation cancellation, CancellationToken ct) =>
        await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct) is { } order
            ? (await cancellation.Cancel(order, byAdmin: true, ct)).ToHttp(TypedResults.NoContent)
            : NotFound.ToProblem();
}
