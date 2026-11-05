using System.Net;
using System.Net.Http.Json;

using Ashen.Catalog.Data;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Customers;

/// <summary>The signed-in customer's wishlist (FR-041, User Story 11).</summary>
public class WishlistApiTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record Item(Guid ProductId, string Name, string? Slug, bool IsAvailable);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> NewProduct(string name, bool published = true)
    {
        var variant = await factory.CreateProduct(name, 25m, stock: 5, published);
        return await factory.Query(sp => sp.GetRequiredService<CatalogDbContext>().Products
            .Where(p => p.Variants.Any(v => v.Id == variant)).Select(p => p.Id).SingleAsync());
    }

    private static async Task<List<Item>> Wishlist(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Item>>("/api/me/wishlist", Ct))!;

    private static Task<HttpResponseMessage> Add(HttpClient client, Guid productId) =>
        client.PutAsync($"/api/me/wishlist/{productId}", null, Ct);

    [Fact]
    public async Task Adding_is_idempotent_and_newest_comes_first()
    {
        var (client, _, _) = await Shopper.New(factory);
        var first = await NewProduct("Wished Tee");
        var second = await NewProduct("Wished Cap");

        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, first)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, second)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, first)).StatusCode);

        var items = await Wishlist(client);
        Assert.Equal([second, first], items.Select(i => i.ProductId));
        Assert.All(items, i => Assert.True(i.IsAvailable));
    }

    [Fact]
    public async Task Removing_takes_it_off_and_is_safe_to_repeat()
    {
        var (client, _, _) = await Shopper.New(factory);
        var product = await NewProduct("Unwished Tee");
        await Add(client, product);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/wishlist/{product}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/wishlist/{product}", Ct)).StatusCode);
        Assert.Empty(await Wishlist(client));
    }

    [Fact]
    public async Task Unknown_or_unpublished_products_cannot_be_added()
    {
        var (client, _, _) = await Shopper.New(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Add(client, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Add(client, await NewProduct("Draft Wish", published: false))).StatusCode);
    }

    [Fact]
    public async Task A_product_unpublished_later_is_shown_as_unavailable()
    {
        var (client, _, _) = await Shopper.New(factory);
        var product = await NewProduct("Vanishing Tee");
        await Add(client, product);

        var admin = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/products/{product}/unpublish", null, Ct)).StatusCode);

        var item = Assert.Single(await Wishlist(client));
        Assert.Equal(("Vanishing Tee", false), (item.Name, item.IsAvailable));
    }

    [Fact]
    public async Task Each_customer_sees_only_their_own_wishlist()
    {
        var (owner, _, _) = await Shopper.New(factory);
        var (other, _, _) = await Shopper.New(factory);
        await Add(owner, await NewProduct("Private Wish"));

        Assert.Empty(await Wishlist(other));
    }

    [Fact]
    public async Task Anonymous_visitors_must_sign_in()
    {
        var anonymous = factory.CreateApiClient();
        await anonymous.RefreshXsrfToken();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/wishlist", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Add(anonymous, Guid.NewGuid())).StatusCode);
    }
}
