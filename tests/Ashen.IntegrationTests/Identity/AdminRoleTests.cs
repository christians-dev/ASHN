using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks.Audit;
using Ashen.Identity.Data;
using Ashen.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Identity;

/// <summary>Granting and revoking the Admin role; the last admin can never be removed (FR-014, Story 12).</summary>
public class AdminRoleTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    private const string Password = "correct horse battery";

    private sealed record AdminDto(Guid UserId, string Email, string DisplayName);
    private sealed record Session(Guid? UserId, List<string> Roles);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A real account signed in with its cookie, so role changes go through the same path as in the browser.</summary>
    private async Task<(HttpClient Client, Guid UserId, string Email)> Register(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName = name }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await client.RefreshXsrfToken();
        var session = (await client.GetFromJsonAsync<Session>("/api/auth/session", Ct))!;
        return (client, session.UserId!.Value, email);
    }

    /// <summary>Signs in again, so the cookie carries the account's current roles.</summary>
    private async Task<HttpClient> SignInAgain(string email)
    {
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
        await client.RefreshXsrfToken();
        return client;
    }

    /// <summary>A registered account that holds the Admin role, signed in.</summary>
    private async Task<(HttpClient Client, Guid UserId, string Email)> NewAdmin(HttpClient? grantedBy = null)
    {
        var (_, userId, email) = await Register("admin");
        if (grantedBy is null)
        {
            await factory.Query(async sp =>
            {
                var db = sp.GetRequiredService<AshenIdentityDbContext>();
                db.UserRoles.Add(new() { UserId = userId, RoleId = AshenIdentityDbContext.AdminRoleId });
                return await db.SaveChangesAsync();
            });
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, (await grantedBy.PostAsJsonAsync("/api/admin/admins", new { email }, Ct)).StatusCode);
        }

        return (await SignInAgain(email), userId, email);
    }

    private static async Task<List<AdminDto>> Admins(HttpClient admin) => (await admin.GetFromJsonAsync<List<AdminDto>>("/api/admin/admins", Ct))!;

    /// <summary>Removes every admin except the given ones, so "last admin" tests start from a known count.</summary>
    private Task KeepOnlyAdmins(params Guid[] keep) =>
        factory.Query(sp => sp.GetRequiredService<AshenIdentityDbContext>().UserRoles
            .Where(r => r.RoleId == AshenIdentityDbContext.AdminRoleId && !keep.Contains(r.UserId))
            .ExecuteDeleteAsync());

    [Fact]
    public async Task Granted_by_email_the_customer_reaches_admin_pages_and_loses_them_when_revoked()
    {
        var (admin, _, _) = await NewAdmin();
        var (customer, customerId, email) = await Register("promoted");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/orders", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/admins", new { email = email.ToUpperInvariant() }, Ct)).StatusCode);
        Assert.Contains(await Admins(admin), a => a.UserId == customerId);
        var promoted = await SignInAgain(email);
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/admin/orders", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/admins/{customerId}", Ct)).StatusCode);
        Assert.DoesNotContain(await Admins(admin), a => a.UserId == customerId);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAgain(email)).GetAsync("/api/admin/orders", Ct)).StatusCode);
    }

    [Fact]
    public async Task Granting_twice_is_harmless()
    {
        var (admin, _, _) = await NewAdmin();
        var (_, userId, email) = await Register("twice");

        await admin.PostAsJsonAsync("/api/admin/admins", new { email }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/admin/admins", new { email }, Ct)).StatusCode);
        Assert.Single(await Admins(admin), a => a.UserId == userId);
    }

    [Fact]
    public async Task Unknown_email_or_non_admin_is_not_found()
    {
        var (admin, _, _) = await NewAdmin();
        var (_, customerId, _) = await Register("plain");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/admins", new { email = "nobody@test.local" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/admins/{customerId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_revoke_themselves_or_delete_their_account()
    {
        var (admin, adminId, _) = await NewAdmin();
        await KeepOnlyAdmins(adminId);

        var revoke = await admin.DeleteAsync($"/api/admin/admins/{adminId}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, revoke.StatusCode);
        Assert.Contains("last admin", await revoke.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/account", Ct)).StatusCode);
        Assert.Single(await Admins(admin));
    }

    [Fact]
    public async Task Two_admins_revoking_each_other_at_once_leave_one_admin()
    {
        var (first, firstId, _) = await NewAdmin();
        var (second, secondId, _) = await NewAdmin(grantedBy: first);
        await KeepOnlyAdmins(firstId, secondId);

        var responses = await Task.WhenAll(
            first.DeleteAsync($"/api/admin/admins/{secondId}", Ct),
            second.DeleteAsync($"/api/admin/admins/{firstId}", Ct));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var remaining = await factory.Query(sp => sp.GetRequiredService<AshenIdentityDbContext>().UserRoles
            .CountAsync(r => r.RoleId == AshenIdentityDbContext.AdminRoleId));
        Assert.Equal(1, remaining);
    }

    [Fact]
    public async Task A_deleted_admin_account_gives_up_the_role()
    {
        var (first, firstId, _) = await NewAdmin();
        var (second, secondId, _) = await NewAdmin(grantedBy: first);

        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync("/api/account", Ct)).StatusCode);

        Assert.DoesNotContain(await Admins(first), a => a.UserId == secondId);
        Assert.Contains(await Admins(first), a => a.UserId == firstId);
    }

    [Fact]
    public async Task Grants_and_revokes_are_audited()
    {
        var (admin, _, _) = await NewAdmin();
        var (_, userId, email) = await Register("audited");
        await admin.PostAsJsonAsync("/api/admin/admins", new { email }, Ct);
        await admin.DeleteAsync($"/api/admin/admins/{userId}", Ct);

        var actions = await factory.Query(sp => sp.GetRequiredService<AshenIdentityDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityId == userId.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Equal(["admin.granted", "admin.revoked"], actions.Order());
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/admins" },
        { "POST", "/api/admin/admins" },
        { "DELETE", $"/api/admin/admins/{Guid.Empty}" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Customers_are_forbidden(string method, string path)
    {
        var (client, _, _) = await Register("nosy");

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { email = "x@test.local" }) }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
