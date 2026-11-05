using Ashen.BuildingBlocks.Events;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Ordering.Features;

/// <summary>A deleted account's orders lose the customer's name, phone, street and email; amounts stay for the books (FR-015).</summary>
internal sealed class OnAccountDeletionRequested(OrderingDbContext db) : IIntegrationEventHandler<AccountDeletionRequested>
{
    public async Task Handle(AccountDeletionRequested deleted, CancellationToken cancellationToken)
    {
        foreach (var order in await db.Orders.Where(o => o.CustomerId == deleted.UserId).ToListAsync(cancellationToken))
        {
            order.RemovePersonalData();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
