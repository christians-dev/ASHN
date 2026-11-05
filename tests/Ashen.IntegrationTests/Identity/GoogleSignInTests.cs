using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Ashen.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashen.IntegrationTests.Identity;

/// <summary>Stands in for Google: on challenge it "signs in" the given user and returns to the callback.</summary>
public sealed class FakeGoogleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public static (string Subject, string Email, bool Verified) User { get; set; }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, User.Subject),
            new Claim(ClaimTypes.Email, User.Email),
            new Claim(ClaimTypes.Name, "Google User"),
            new Claim("email_verified", User.Verified ? "true" : "false"),
        ], Scheme.Name);

        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
        Response.Redirect(properties.RedirectUri!);
    }
}

public sealed class GoogleFactory : AshenApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeGoogleHandler>("Google", "Google", _ => { }));
    }
}

public class GoogleSignInTests(GoogleFactory factory) : IClassFixture<GoogleFactory>
{
    private sealed record Session(bool IsAuthenticated, string? Email);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpResponseMessage Response, Session Session)> SignInWithGoogle(string subject, string email, bool verified, string returnUrl = "/account")
    {
        FakeGoogleHandler.User = (subject, email, verified);
        var client = factory.CreateApiClient();
        var response = await client.GetAsync($"/api/auth/google?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);
        return (response, (await client.GetFromJsonAsync<Session>("/api/auth/session", Ct))!);
    }

    [Fact]
    public async Task First_sign_in_creates_an_account_and_returns_to_the_page()
    {
        var (response, session) = await SignInWithGoogle("g-new", "fresh@gmail.test", verified: true);

        Assert.True(session.IsAuthenticated);
        Assert.Equal("fresh@gmail.test", session.Email);
        Assert.Equal("/account", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Verified_email_links_to_the_existing_account()
    {
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        await client.PostAsJsonAsync("/api/auth/register", new { email = "both@gmail.test", password = "correct horse battery", displayName = "Both" }, Ct);

        var (_, session) = await SignInWithGoogle("g-both", "both@gmail.test", verified: true);
        var (_, again) = await SignInWithGoogle("g-both", "both@gmail.test", verified: true);

        Assert.True(session.IsAuthenticated);
        Assert.True(again.IsAuthenticated); // second time it signs straight in via the stored login
    }

    [Fact]
    public async Task Google_takes_over_an_unproven_password_account_so_a_squatter_loses_access()
    {
        // An attacker registers someone else's address with a password before its owner ever signs in.
        var attacker = factory.CreateApiClient();
        await attacker.RefreshXsrfToken();
        await attacker.PostAsJsonAsync("/api/auth/register", new { email = "squatted@gmail.test", password = "attacker password 1", displayName = "Squatter" }, Ct);

        var (_, session) = await SignInWithGoogle("g-owner", "squatted@gmail.test", verified: true);

        Assert.True(session.IsAuthenticated); // its other sessions end at the next security stamp check (1 minute)
        var retry = factory.CreateApiClient();
        await retry.RefreshXsrfToken();
        var signIn = await retry.PostAsJsonAsync("/api/auth/login", new { email = "squatted@gmail.test", password = "attacker password 1" }, Ct);
        Assert.False(signIn.IsSuccessStatusCode); // and the password no longer works
    }

    [Fact]
    public async Task Unverified_email_never_links_or_signs_in()
    {
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        await client.PostAsJsonAsync("/api/auth/register", new { email = "victim@gmail.test", password = "correct horse battery", displayName = "Victim" }, Ct);

        var (response, session) = await SignInWithGoogle("g-attacker", "victim@gmail.test", verified: false);

        Assert.False(session.IsAuthenticated);
        Assert.Equal("/sign-in", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData(@"/\evil.example")]
    public async Task Non_local_return_url_is_rejected(string returnUrl)
    {
        var response = await factory.CreateApiClient().GetAsync($"/api/auth/google?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
