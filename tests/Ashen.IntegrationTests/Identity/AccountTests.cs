using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using Ashen.IntegrationTests.Infrastructure;

namespace Ashen.IntegrationTests.Identity;

public partial class AccountTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    private const string Password = "correct horse battery";

    private sealed record Session(bool IsAuthenticated, Guid? UserId, string? Email, string? DisplayName, List<string> Roles);
    private sealed record AccountDto(string Email, string DisplayName);
    private sealed record Problem(int Status, Dictionary<string, string[]>? Errors);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AnonymousClient()
    {
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        return client;
    }

    private async Task<HttpClient> Register(string email, string password = Password, string displayName = "Alex")
    {
        var client = await AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await client.RefreshXsrfToken(); // the anti-forgery token belongs to the signed-in user now
        return client;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private static async Task<Session> SessionOf(HttpClient client) =>
        (await client.GetFromJsonAsync<Session>("/api/auth/session", Ct))!;

    [Fact]
    public async Task Register_creates_a_signed_in_customer()
    {
        var client = await Register("new@test.local", displayName: "Sam");

        var session = await SessionOf(client);
        Assert.True(session.IsAuthenticated);
        Assert.Equal("new@test.local", session.Email);
        Assert.Equal("Sam", session.DisplayName);
        Assert.Equal(["Customer"], session.Roles);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_and_weak_passwords()
    {
        await Register("taken@test.local");
        var client = await AnonymousClient();

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email = "TAKEN@test.local", password = Password, displayName = "X" }, Ct);
        var tooShort = await client.PostAsJsonAsync("/api/auth/register", new { email = "short@test.local", password = "abc", displayName = "X" }, Ct);
        var common = await client.PostAsJsonAsync("/api/auth/register", new { email = "common@test.local", password = "password", displayName = "X" }, Ct);

        Assert.Contains("email", (await duplicate.ReadAs<Problem>()).Errors!.Keys);
        Assert.Contains("password", (await tooShort.ReadAs<Problem>()).Errors!.Keys);
        Assert.Contains("password", (await common.ReadAs<Problem>()).Errors!.Keys);
        Assert.All([duplicate, tooShort, common], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Login_and_logout()
    {
        await Register("login@test.local");
        var client = await AnonymousClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client, "login@test.local", "wrong password")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(client, "login@test.local", Password)).StatusCode);
        await client.RefreshXsrfToken();
        Assert.True((await SessionOf(client)).IsAuthenticated);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null, Ct)).StatusCode);
        Assert.False((await SessionOf(client)).IsAuthenticated);
    }

    [Fact]
    public async Task Sixth_attempt_after_five_failures_is_locked_out_even_with_the_right_password()
    {
        await Register("lockout@test.local");
        var client = await AnonymousClient();

        for (var i = 0; i < 5; i++)
        {
            await Login(client, "lockout@test.local", "wrong password");
        }

        var sixth = await Login(client, "lockout@test.local", Password);

        Assert.Equal(HttpStatusCode.Locked, sixth.StatusCode);
    }

    [Fact]
    public async Task Password_reset_link_works_once()
    {
        await Register("reset@test.local");
        var client = await AnonymousClient();

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/password/forgot", new { email = "nobody@test.local" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/password/forgot", new { email = "reset@test.local" }, Ct)).StatusCode);
        Assert.Empty(factory.Emails.To("nobody@test.local"));

        var link = ResetLink().Match(factory.Emails.To("reset@test.local").Last().TextBody);
        Assert.True(link.Success);
        Assert.StartsWith("https://localhost/reset-password?", link.Value, StringComparison.Ordinal);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link.Value).Query);
        var reset = new { email = query["email"], token = query["token"], newPassword = "a brand new passphrase" };

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/password/reset", reset, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password/reset", reset, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(client, "reset@test.local", "a brand new passphrase")).StatusCode);
    }

    [Fact]
    public async Task Initial_admin_email_gets_the_admin_role()
    {
        var client = await Register(AshenApiFactory.InitialAdminEmail);

        Assert.Contains("Admin", (await SessionOf(client)).Roles);
    }

    [Fact]
    public async Task Customer_can_update_name_and_email()
    {
        var client = await Register("before@test.local", displayName: "Before");

        var update = await client.PutAsJsonAsync("/api/account", new { email = "after@test.local", displayName = "After" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(new AccountDto("after@test.local", "After"), await client.GetFromJsonAsync<AccountDto>("/api/account", Ct));
        Assert.Equal("After", (await SessionOf(client)).DisplayName);
    }

    [Fact]
    public async Task Deleting_the_account_signs_out_and_blocks_sign_in()
    {
        var client = await Register("leaving@test.local");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account", Ct)).StatusCode);

        Assert.False((await SessionOf(client)).IsAuthenticated);
        var again = await AnonymousClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(again, "leaving@test.local", Password)).StatusCode);
        // The email is free again: the old account was anonymised.
        await Register("leaving@test.local");
    }

    [Fact]
    public async Task Account_endpoints_require_sign_in()
    {
        var client = await AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account", Ct)).StatusCode);
    }

    [GeneratedRegex(@"https://\S+/reset-password\?\S+")]
    private static partial Regex ResetLink();
}
