using Ashen.BuildingBlocks.Events;
using Ashen.Identity.Contracts;
using Ashen.Payments.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Payments.Features;

/// <summary>A deleted account's saved cards are removed for good. Payment records stay, holding only brand and last four.</summary>
internal sealed class OnAccountDeletionRequested(PaymentsDbContext db) : IIntegrationEventHandler<AccountDeletionRequested>
{
    public Task Handle(AccountDeletionRequested deleted, CancellationToken cancellationToken) =>
        db.SavedCards.Where(c => c.CustomerId == deleted.UserId).ExecuteDeleteAsync(cancellationToken);
}
