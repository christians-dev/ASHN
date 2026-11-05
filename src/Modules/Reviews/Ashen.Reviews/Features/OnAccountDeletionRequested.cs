using Ashen.BuildingBlocks.Events;
using Ashen.Identity.Contracts;
using Ashen.Reviews.Data;
using Ashen.Reviews.Domain;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Reviews.Features;

/// <summary>A deleted account's reviews stay, credited to "Former customer".</summary>
internal sealed class OnAccountDeletionRequested(ReviewsDbContext db) : IIntegrationEventHandler<AccountDeletionRequested>
{
    public Task Handle(AccountDeletionRequested deleted, CancellationToken cancellationToken) =>
        db.Reviews.Where(r => r.CustomerId == deleted.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AuthorDisplayName, Review.FormerCustomer), cancellationToken);
}
