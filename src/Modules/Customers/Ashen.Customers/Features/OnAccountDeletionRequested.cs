using Ashen.BuildingBlocks.Events;
using Ashen.Customers.Data;
using Ashen.Identity.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Customers.Features;

/// <summary>A deleted account's addresses and wishlist are removed for good (FR-015).</summary>
internal sealed class OnAccountDeletionRequested(CustomersDbContext db) : IIntegrationEventHandler<AccountDeletionRequested>
{
    public Task Handle(AccountDeletionRequested integrationEvent, CancellationToken cancellationToken) =>
        db.Profiles.Where(p => p.Id == integrationEvent.UserId).ExecuteDeleteAsync(cancellationToken);
}
