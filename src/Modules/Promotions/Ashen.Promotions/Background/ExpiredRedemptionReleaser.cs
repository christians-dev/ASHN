using Ashen.BuildingBlocks.Background;
using Ashen.Promotions.Contracts;
using Ashen.Promotions.Data;
using Ashen.Promotions.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashen.Promotions.Background;

/// <summary>
/// Gives back coupon uses held by checkouts that never finished within 15 minutes (e.g. a crash mid-checkout),
/// so an abandoned attempt can't use up a limited coupon. Checkout normally releases them itself.
/// </summary>
internal sealed partial class ExpiredRedemptionReleaser(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExpiredRedemptionReleaser> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        PeriodicJob.RunEveryMinute(clock, logger, ReleaseExpired, stoppingToken);

    /// <returns>How many held uses were released.</returns>
    public async Task<int> ReleaseExpired(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        var promotions = scope.ServiceProvider.GetRequiredService<IPromotionsModule>();

        var now = clock.GetUtcNow();
        var expired = await db.Redemptions
            .Where(r => r.Status == RedemptionStatus.Pending && r.ExpiresAt < now)
            .Select(r => r.CheckoutId)
            .ToListAsync(cancellationToken);

        foreach (var checkoutId in expired)
        {
            await promotions.ReleaseCoupon(checkoutId, cancellationToken);
            LogReleased(checkoutId);
        }

        return expired.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Released expired coupon hold for checkout {CheckoutId}")]
    private partial void LogReleased(Guid checkoutId);
}
