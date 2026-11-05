using System.Net;
using System.Text.RegularExpressions;

using Ashen.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Platform;

/// <summary>
/// SC-008: every admin route the app actually registers refuses customers and anonymous callers, including routes
/// added later that no story's hand-written list knows about.
/// </summary>
public partial class AdminRouteSweepTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    private List<(string Method, string Path, string? ContentType)> AdminRoutes() =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/admin", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, "/" + RouteParameter().Replace(e.RoutePattern.RawText!.TrimStart('/'), p =>
                    p.Value.Contains("int") ? "1" : Guid.Empty.ToString()),
                    // Routing skips an endpoint whose body type doesn't match, so send the one it declares.
                    e.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault())))];

    [Fact]
    public async Task Every_admin_route_refuses_customers_and_anonymous_callers()
    {
        var anonymous = factory.CreateApiClient();
        await anonymous.RefreshXsrfToken(); // so writes reach the authorization check rather than fail anti-forgery
        var customer = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Customer");
        var routes = AdminRoutes();

        var wrong = new List<string>();
        foreach (var (method, path, contentType) in routes)
        {
            foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (customer, HttpStatusCode.Forbidden) })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (contentType is not null)
                {
                    request.Content = new StringContent("{}");
                    request.Content.Headers.ContentType = new(contentType);
                }

                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                if (response.StatusCode != expected)
                {
                    wrong.Add($"{method} {path}: {(int)response.StatusCode}, expected {(int)expected}");
                }
            }
        }

        Assert.True(routes.Count >= 20, $"Found only {routes.Count} admin routes; the sweep may be missing them.");
        Assert.Empty(wrong);
    }
}
