namespace Ashen.Identity.Contracts;

/// <summary>The signed-in user for the current request, if any.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    string? DisplayName { get; }
    string? Email { get; }
}

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public static class AuthorizationPolicies
{
    /// <summary>Use on every admin endpoint: <c>.RequireAuthorization(AuthorizationPolicies.Admin)</c>.</summary>
    public const string Admin = "Admin";
}
