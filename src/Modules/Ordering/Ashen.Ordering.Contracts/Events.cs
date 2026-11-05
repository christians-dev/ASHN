using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Events;

namespace Ashen.Ordering.Contracts;

/// <summary>An order was placed and paid; the customer gets a confirmation email.</summary>
public sealed record OrderPlaced(Guid OrderId, string OrderNumber, Guid CustomerId, string Email, Money Total) : IIntegrationEvent;

/// <summary>An order left the warehouse; the customer gets a shipping email.</summary>
public sealed record OrderShipped(Guid OrderId, string OrderNumber, string Email) : IIntegrationEvent;
