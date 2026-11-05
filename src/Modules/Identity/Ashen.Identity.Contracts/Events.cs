using Ashen.BuildingBlocks.Events;

namespace Ashen.Identity.Contracts;

/// <summary>A customer deleted their account: other modules remove or anonymise what they hold about them.</summary>
public sealed record AccountDeletionRequested(Guid UserId) : IIntegrationEvent;
