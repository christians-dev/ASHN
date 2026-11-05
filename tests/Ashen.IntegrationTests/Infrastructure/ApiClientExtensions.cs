using System.Net.Http.Json;

namespace Ashen.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    public const string XsrfHeader = "X-XSRF-TOKEN";

    /// <summary>Acts as the given user for every following request (see <see cref="TestAuthHandler"/>).</summary>
    public static async Task<HttpClient> ActAs(this HttpClient client, Guid userId, params string[] roles)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserHeader);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        // Anti-forgery tokens belong to a user, so fetch a fresh one after switching.
        await client.RefreshXsrfToken();
        return client;
    }

    /// <summary>Calls <c>/api/auth/session</c> like the SPA does and sends its token on later writes.</summary>
    public static async Task RefreshXsrfToken(this HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/session");
        response.EnsureSuccessStatusCode();

        var token = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0])
                .Where(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
                .Select(c => Uri.UnescapeDataString(c["XSRF-TOKEN=".Length..]))
                .LastOrDefault()
            : null;

        client.DefaultRequestHeaders.Remove(XsrfHeader);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add(XsrfHeader, token);
        }
    }

    public static async Task<T> ReadAs<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;
}
