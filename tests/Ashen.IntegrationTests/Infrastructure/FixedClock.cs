namespace Ashen.IntegrationTests.Infrastructure;

/// <summary>A clock frozen at a chosen moment, for testing time-based jobs.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
