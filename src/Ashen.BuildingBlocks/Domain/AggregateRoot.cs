using Ashen.BuildingBlocks.Events;

namespace Ashen.BuildingBlocks.Domain;

public abstract class Entity
{
    // Time-ordered ids keep inserts index-friendly.
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

public interface IHasIntegrationEvents
{
    IReadOnlyList<IIntegrationEvent> PendingEvents { get; }
    void ClearPendingEvents();
}

/// <summary>Entry point to a cluster of objects saved together; the only way to change them (constitution V).</summary>
public abstract class AggregateRoot : Entity, IHasIntegrationEvents
{
    private readonly List<IIntegrationEvent> _pendingEvents = [];

    public IReadOnlyList<IIntegrationEvent> PendingEvents => _pendingEvents;

    /// <summary>Queues an event; it is published only after the aggregate is saved.</summary>
    protected void Raise(IIntegrationEvent integrationEvent) => _pendingEvents.Add(integrationEvent);

    public void ClearPendingEvents() => _pendingEvents.Clear();
}
