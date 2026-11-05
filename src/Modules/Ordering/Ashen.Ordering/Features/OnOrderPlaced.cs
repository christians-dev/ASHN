using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Email;
using Ashen.BuildingBlocks.Events;
using Ashen.Ordering.Contracts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashen.Ordering.Features;

/// <summary>Emails the order confirmation (FR-023).</summary>
internal sealed partial class OnOrderPlaced(IEmailSender email, IOptions<StoreOptions> store, ILogger<OnOrderPlaced> logger)
    : IIntegrationEventHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced placed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(placed.Email))
        {
            return;
        }

        var link = store.Value.Link($"/account/orders/{placed.OrderId}");
        await email.Send(new EmailMessage(
            placed.Email,
            $"Your {store.Value.StoreName} order {placed.OrderNumber}",
            $"Thank you for your order {placed.OrderNumber}.\n\nTotal paid: {placed.Total.Amount:0.00} {placed.Total.Currency}\n\nSee your order: {link}\n\n{store.Value.StoreName}"),
            cancellationToken);
        LogSent(placed.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order confirmation email sent for order {OrderId}")]
    private partial void LogSent(Guid orderId);
}
