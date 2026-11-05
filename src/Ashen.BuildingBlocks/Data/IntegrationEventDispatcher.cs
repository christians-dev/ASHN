using Ashen.BuildingBlocks.Domain;
using Ashen.BuildingBlocks.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ashen.BuildingBlocks.Data;

/// <summary>Publishes aggregates' pending integration events after their changes are saved.</summary>
public sealed class IntegrationEventDispatcher(IEventBus eventBus) : SaveChangesInterceptor
{
    private List<IIntegrationEvent> _collected = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } db)
        {
            var aggregates = db.ChangeTracker.Entries<IHasIntegrationEvents>()
                .Select(e => e.Entity)
                .Where(a => a.PendingEvents.Count > 0)
                .ToList();

            _collected = aggregates.SelectMany(a => a.PendingEvents).ToList();
            aggregates.ForEach(a => a.ClearPendingEvents());
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var events = _collected;
        _collected = [];

        foreach (var integrationEvent in events)
        {
            await eventBus.Publish(integrationEvent, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        // Nothing was saved, so nothing happened: drop the events.
        _collected = [];
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
