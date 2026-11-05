using Ashen.Ordering.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Ordering;

internal sealed class OrderingModuleApi(OrderingDbContext db) : IOrderingModule
{
    public Task<bool> HasDeliveredProduct(Guid customerId, Guid productId, CancellationToken cancellationToken = default) =>
        db.Orders.AnyAsync(
            o => o.CustomerId == customerId && o.Status == OrderStatus.Delivered && o.Lines.Any(l => l.ProductId == productId),
            cancellationToken);
}
