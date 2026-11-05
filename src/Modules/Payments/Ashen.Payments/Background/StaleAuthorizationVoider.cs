using Ashen.Payments.Contracts;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ashen.Payments.Background;

/// <summary>
/// Voids authorisations that never became an order within 15 minutes, e.g. after a crash between authorising
/// and saving the order (FR-021). Checkout normally voids them itself; this is the safety net.
/// </summary>
internal sealed partial class StaleAuthorizationVoider(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<StaleAuthorizationVoider> logger) : BackgroundService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await VoidStale(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    /// <returns>How many payments were voided.</returns>
    public async Task<int> VoidStale(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentsModule>();

        var cutoff = clock.GetUtcNow() - MaxAge;
        var stale = await db.Payments
            .Where(p => p.Status == PaymentStatus.Authorized && p.OrderId == null && p.CreatedAt < cutoff)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in stale)
        {
            await payments.Void(id, cancellationToken);
            LogVoided(id);
        }

        return stale.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Voided stale payment authorization {PaymentId} with no order")]
    private partial void LogVoided(Guid paymentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Voiding stale payment authorizations failed")]
    private partial void LogFailed(Exception ex);
}
