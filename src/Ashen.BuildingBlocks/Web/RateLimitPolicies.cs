namespace Ashen.BuildingBlocks.Web;

public static class RateLimitPolicies
{
    /// <summary>Sign-in, registration and password reset: per IP, <c>Ashen:RateLimits:AuthPerMinute</c> (default 10).</summary>
    public const string Auth = "auth";
}
