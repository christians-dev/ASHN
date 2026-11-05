using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Email;
using Ashen.BuildingBlocks.Events;
using Ashen.Ordering.Contracts;

using Microsoft.Extensions.Options;

namespace Ashen.Ordering.Features;

/// <summary>Tells the customer their order is on its way.</summary>
internal sealed class OnOrderShipped(IEmailSender email, IOptions<StoreOptions> store) : IIntegrationEventHandler<OrderShipped>
{
    public Task Handle(OrderShipped shipped, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(shipped.Email)
            ? Task.CompletedTask
            : email.Send(
                new EmailMessage(
                    shipped.Email,
                    $"Your {store.Value.StoreName} order {shipped.OrderNumber} has shipped",
                    $"Good news: order {shipped.OrderNumber} is on its way.\n\nSee your order: {store.Value.Link($"/account/orders/{shipped.OrderId}")}\n\n{store.Value.StoreName}"),
                cancellationToken);
}
