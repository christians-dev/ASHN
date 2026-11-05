namespace Ashen.BuildingBlocks.Events;

/// <summary>A fact one module tells other modules about, e.g. "order placed".</summary>
public interface IIntegrationEvent;

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}

public interface IEventBus
{
    Task Publish(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
