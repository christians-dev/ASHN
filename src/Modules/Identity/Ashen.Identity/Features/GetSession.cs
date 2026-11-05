using System.Security.Claims;

using Ashen.Identity.Contracts;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Identity.Features;

/// <summary>Who is signed in, plus a fresh anti-forgery cookie the SPA echoes back on writes.</summary>
internal static class GetSession
{
    public sealed record SessionResponse(
        bool IsAuthenticated, Guid? UserId, string? Email, string? DisplayName, IReadOnlyList<string> Roles);

    public const string XsrfCookieName = "XSRF-TOKEN";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/auth/session", Handle)
            .AllowAnonymous()
            .WithTags("Auth");

    private static Ok<SessionResponse> Handle(HttpContext http, ICurrentUser currentUser, IAntiforgery antiforgery)
    {
        IssueXsrfCookie(http, antiforgery);

        var user = http.User;
        return TypedResults.Ok(new SessionResponse(
            currentUser.IsAuthenticated,
            currentUser.UserId,
            user.FindFirstValue(ClaimTypes.Email),
            currentUser.DisplayName,
            user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()));
    }

    /// <summary>The token is tied to the signed-in user, so the SPA must call this again after signing in or out.</summary>
    public static void IssueXsrfCookie(HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // the SPA reads it and sends it back in the X-XSRF-TOKEN header
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }
}
