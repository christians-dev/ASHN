using System.Security.Claims;

using Ashen.Identity.Contracts;

using Microsoft.AspNetCore.Http;

namespace Ashen.Identity;

internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) == true;

    public string? DisplayName => Principal?.FindFirstValue(AccountClaims.DisplayName);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
}

internal static class AccountClaims
{
    public const string DisplayName = "display_name";
}
