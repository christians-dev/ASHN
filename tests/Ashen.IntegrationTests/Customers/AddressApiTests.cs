using System.Net;
using System.Net.Http.Json;

using Ashen.Customers.Contracts;
using Ashen.IntegrationTests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Customers;

public class AddressApiTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    private sealed record AddressDto(Guid Id, string RecipientName, string StateCode, string PostalCode, bool IsDefault);
    private sealed record Problem(Dictionary<string, string[]>? Errors);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Address(string recipient = "Sam Lee", string state = "IL", string zip = "60601", string country = "US") => new
    {
        recipientName = recipient,
        line1 = "1 Main St",
        line2 = (string?)null,
        city = "Chicago",
        stateCode = state,
        postalCode = zip,
        countryCode = country,
        phone = "+1 312 555 0100",
    };

    private async Task<HttpClient> Customer(Guid? id = null) => await factory.CreateApiClient().ActAs(id ?? Guid.CreateVersion7(), "Customer");

    private static async Task<AddressDto> Add(HttpClient client, object address)
    {
        var response = await client.PostAsJsonAsync("/api/me/addresses", address, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAs<AddressDto>();
    }

    private static async Task<List<AddressDto>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AddressDto>>("/api/me/addresses", Ct))!;

    [Fact]
    public async Task Add_list_edit_and_delete()
    {
        var client = await Customer();
        Assert.Empty(await List(client));

        var home = await Add(client, Address("Home"));
        var work = await Add(client, Address("Work", state: "TX", zip: "73301"));
        Assert.True(home.IsDefault);
        Assert.False(work.IsDefault);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/me/addresses/{work.Id}", Address("Office", state: "tx", zip: "73301-0001"), Ct)).StatusCode);
        var edited = (await List(client)).Single(a => a.Id == work.Id);
        Assert.Equal(("Office", "TX", "73301-0001"), (edited.RecipientName, edited.StateCode, edited.PostalCode));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/addresses/{work.Id}", Ct)).StatusCode);
        Assert.Equal([home.Id], (await List(client)).Select(a => a.Id));
    }

    [Fact]
    public async Task Default_can_be_moved_and_survives_deleting_the_default()
    {
        var client = await Customer();
        var first = await Add(client, Address("First"));
        var second = await Add(client, Address("Second"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/addresses/{second.Id}/default", null, Ct)).StatusCode);
        Assert.Equal(second.Id, (await List(client)).Single(a => a.IsDefault).Id);

        await client.DeleteAsync($"/api/me/addresses/{second.Id}", Ct);
        Assert.Equal(first.Id, (await List(client)).Single(a => a.IsDefault).Id);
    }

    [Fact]
    public async Task Invalid_addresses_are_rejected_field_by_field()
    {
        var client = await Customer();

        var canada = await client.PostAsJsonAsync("/api/me/addresses", Address(country: "CA"), Ct);
        var badZip = await client.PostAsJsonAsync("/api/me/addresses", Address(zip: "6060"), Ct);
        var badState = await client.PostAsJsonAsync("/api/me/addresses", Address(state: "ZZ"), Ct);

        Assert.Contains("countryCode", (await canada.ReadAs<Problem>()).Errors!.Keys);
        Assert.Contains("postalCode", (await badZip.ReadAs<Problem>()).Errors!.Keys);
        Assert.Contains("stateCode", (await badState.ReadAs<Problem>()).Errors!.Keys);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Another_customers_address_is_not_found()
    {
        var owner = await Customer();
        var address = await Add(owner, Address());
        var other = await Customer();

        Assert.Empty(await List(other));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/me/addresses/{address.Id}", Address("Mine now"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/me/addresses/{address.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/me/addresses/{address.Id}/default", null, Ct)).StatusCode);
        Assert.Single(await List(owner));
    }

    [Fact]
    public async Task Contract_returns_only_the_customers_own_address()
    {
        var ownerId = Guid.CreateVersion7();
        var address = await Add(await Customer(ownerId), Address("Contract Test"));

        using var scope = factory.Services.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomersModule>();

        Assert.Equal("Contract Test", (await customers.GetAddress(ownerId, address.Id, Ct))!.RecipientName);
        Assert.Null(await customers.GetAddress(Guid.CreateVersion7(), address.Id, Ct));
    }

    [Fact]
    public async Task Addresses_require_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync("/api/me/addresses", Ct)).StatusCode);
    }
}
