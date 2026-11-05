using System.Net;
using System.Net.Http.Json;

using Ashen.IntegrationTests.Infrastructure;

namespace Ashen.IntegrationTests.Platform;

public class SecurityDefaultsTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    private sealed record Session(bool IsAuthenticated, Guid? UserId, IReadOnlyList<string> Roles);

    [Fact]
    public async Task Session_is_anonymous_and_issues_an_antiforgery_cookie()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.ReadAs<Session>()).IsAuthenticated);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Write_without_antiforgery_header_is_rejected()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/session", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Write_with_antiforgery_header_passes_the_check()
    {
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();

        var response = await client.PostAsJsonAsync("/api/auth/session", new { }, TestContext.Current.CancellationToken);

        // Past the anti-forgery check; no POST route exists, so the API's 404 catch-all answers.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_is_a_404_problem_not_the_spa()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Non_api_route_returns_the_spa_page()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/products/some-hoodie", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/region", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Region_is_seeded_for_the_united_states()
    {
        var client = factory.CreateApiClient();

        var region = await client.GetFromJsonAsync<RegionResponse>("/api/region", TestContext.Current.CancellationToken);

        Assert.Equal("US", region!.CountryCode);
        Assert.Equal("USD", region.CurrencyCode);
        Assert.Contains(region.States, s => s.Code == "IL");
    }

    private sealed record RegionResponse(string CountryCode, string CurrencyCode, IReadOnlyList<State> States);
    private sealed record State(string Code, string Name);
}

public class MissingStoreBaseUrlFactory : AshenApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?> { ["Ashen:Store:BaseUrl"] = "" };
}

public class StartupValidationTests(MissingStoreBaseUrlFactory factory) : IClassFixture<MissingStoreBaseUrlFactory>
{
    [Fact]
    public void Host_refuses_to_start_without_required_store_settings()
    {
        // ValidateOnStart: a misconfigured app fails at start-up, not on first use (constitution IX).
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
        Assert.Contains("BaseUrl", error.ToString(), StringComparison.Ordinal);
    }
}
