using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ashen.BuildingBlocks.Events;

// shortcut: events not persisted, upgrade to outbox when a module is extracted (research R8)
internal sealed partial class InProcessEventBus(IServiceProvider services, ILogger<InProcessEventBus> logger) : IEventBus
{
    public async Task Publish(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        // Call through the public interface method: handler classes are internal to their modules.
        var handle = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.Handle))!;

        foreach (var handler in services.GetServices(handlerType))
        {
            try
            {
                await (Task)handle.Invoke(handler, [integrationEvent, cancellationToken])!;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The publisher's change is already committed; a failing handler must not undo or hide it.
                LogHandlerFailed(ex, handler!.GetType().Name, integrationEvent.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Integration event handler {Handler} failed for {Event}")]
    private partial void LogHandlerFailed(Exception ex, string handler, string @event);
}
