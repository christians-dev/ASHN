using Ashen.Catalog.Contracts;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashen.Catalog.Background;

/// <summary>Puts stock back on sale when a checkout held it for over 15 minutes, e.g. after a crash mid-checkout.</summary>
internal sealed partial class ExpiredReservationReleaser(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExpiredReservationReleaser> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReleaseExpired(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    public async Task ReleaseExpired(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogModule>();

        var now = clock.GetUtcNow();
        var checkouts = await db.Set<StockReservation>()
            .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAt < now)
            .Select(r => r.CheckoutId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var checkoutId in checkouts)
        {
            await catalog.ReleaseStock(checkoutId, cancellationToken);
            LogExpired(checkoutId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Released expired stock reservations for checkout {CheckoutId}")]
    private partial void LogExpired(Guid checkoutId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing expired stock reservations failed")]
    private partial void LogFailed(Exception ex);
}
