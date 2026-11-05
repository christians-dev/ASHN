using Microsoft.Extensions.Logging;

namespace Ashen.BuildingBlocks.Background;

/// <summary>The loop shared by the modules' safety-net jobs: run once a minute, log a failure and try again next time.</summary>
public static partial class PeriodicJob
{
    public static async Task RunEveryMinute(TimeProvider clock, ILogger logger, Func<CancellationToken, Task> runOnce, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await runOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background job run failed; it runs again in a minute")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
