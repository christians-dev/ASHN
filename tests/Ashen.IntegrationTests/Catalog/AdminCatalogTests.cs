using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Ashen.BuildingBlocks.Audit;
using Ashen.Catalog.Data;
using Ashen.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Catalog;

public class AdminCatalogTests(AshenApiFactory factory) : IClassFixture<AshenApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private sealed record Created(Guid Id);
    private sealed record CategoryDto(Guid Id, string Name, string Slug);
    private sealed record Variant(Guid Id, string Sku, int StockOnHand);
    private sealed record Image(Guid Id, string Url, int SortOrder);
    private sealed record AdminProduct(Guid Id, string Slug, string Status, List<Variant> Variants, List<Image> Images);
    private sealed record PublicVariant(string Sku, bool IsAvailable);
    private sealed record PublicProduct(List<PublicVariant> Variants);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> Admin() => await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");

    private static async Task<Guid> NewCategory(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/categories", new { name }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAs<Created>()).Id;
    }

    /// <summary>A draft with Size S/M, one variant (S, $40, 5 in stock) and no image.</summary>
    private static async Task<Guid> NewDraft(HttpClient admin, Guid categoryId, string name)
    {
        var created = await admin.PostAsJsonAsync("/api/admin/products", new { name, description = "Test product.", categoryId }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadAs<Created>()).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/products/{id}/options",
            new { options = new[] { new { name = "Size", values = new[] { "S", "M" } } } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddVariant(admin, id, $"{name}-S", "S")).StatusCode);
        return id;
    }

    private static Task<HttpResponseMessage> AddVariant(HttpClient admin, Guid productId, string sku, string size) =>
        admin.PostAsJsonAsync($"/api/admin/products/{productId}/variants",
            new { sku, optionValues = new Dictionary<string, string> { ["Size"] = size }, price = 40m, stockOnHand = 5 }, Ct);

    private static async Task<HttpResponseMessage> UploadImage(HttpClient admin, Guid productId, byte[] bytes, string fileName = "photo.png")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);
        content.Add(new StringContent("Front view"), "altText");
        return await admin.PostAsync($"/api/admin/products/{productId}/images", content, Ct);
    }

    private static async Task<AdminProduct> Get(HttpClient admin, Guid id) =>
        (await admin.GetFromJsonAsync<AdminProduct>($"/api/admin/products/{id}", Ct))!;

    [Fact]
    public async Task Draft_is_hidden_until_published()
    {
        var admin = await Admin();
        var id = await NewDraft(admin, await NewCategory(admin, "Tees"), "Plain Tee");
        var slug = (await Get(admin, id)).Slug;
        var shopper = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.NotFound, (await shopper.GetAsync($"/api/products/{slug}", Ct)).StatusCode);

        // Publishing needs an image.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/admin/products/{id}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UploadImage(admin, id, Png)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/products/{id}/publish", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await shopper.GetAsync($"/api/products/{slug}", Ct)).StatusCode);
        Assert.Equal("Published", (await Get(admin, id)).Status);
    }

    [Fact]
    public async Task Upload_rejects_a_file_that_is_not_really_an_image()
    {
        var admin = await Admin();
        var id = await NewDraft(admin, await NewCategory(admin, "Caps"), "Plain Cap");

        var response = await UploadImage(admin, id, "<svg onload=alert(1)>"u8.ToArray(), "evil.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await Get(admin, id)).Images);
    }

    [Fact]
    public async Task Stock_set_to_zero_makes_the_variant_unavailable()
    {
        var admin = await Admin();
        var id = await NewDraft(admin, await NewCategory(admin, "Socks"), "Plain Sock");
        await UploadImage(admin, id, Png);
        await admin.PostAsync($"/api/admin/products/{id}/publish", null, Ct);
        var product = await Get(admin, id);

        var response = await admin.PutAsJsonAsync($"/api/admin/products/{id}/variants/{product.Variants[0].Id}/stock", new { stockOnHand = 0 }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var shown = await factory.CreateApiClient().GetFromJsonAsync<PublicProduct>($"/api/products/{product.Slug}", Ct);
        Assert.False(shown!.Variants.Single().IsAvailable);
    }

    [Fact]
    public async Task Option_rules_are_enforced()
    {
        var admin = await Admin();
        var id = await NewDraft(admin, await NewCategory(admin, "Belts"), "Plain Belt");

        Assert.Equal(HttpStatusCode.BadRequest, (await AddVariant(admin, id, "BELT-XL", "XL")).StatusCode);   // not an allowed value
        Assert.Equal(HttpStatusCode.Conflict, (await AddVariant(admin, id, "BELT-S2", "S")).StatusCode);     // same combination
        Assert.Equal(HttpStatusCode.Conflict, (await AddVariant(admin, id, "Plain Belt-S", "M")).StatusCode); // SKU already used

        var addOption = await admin.PutAsJsonAsync($"/api/admin/products/{id}/options",
            new { options = new[] { new { name = "Size", values = new[] { "S", "M" } }, new { name = "Colour", values = new[] { "Black" } } } }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, addOption.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_ordered_product_archives_it_and_an_unordered_one_is_removed()
    {
        var admin = await Admin();
        var category = await NewCategory(admin, "Bags");
        var ordered = await NewDraft(admin, category, "Tote Bag");
        var unordered = await NewDraft(admin, category, "Duffel Bag");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await db.Products.SingleAsync(p => p.Id == ordered, Ct)).MarkOrdered(); // stands in for a checkout (US5)
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/products/{ordered}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/products/{unordered}", Ct)).StatusCode);

        Assert.Equal("Archived", (await Get(admin, ordered)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/products/{unordered}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Category_with_products_cannot_be_deleted()
    {
        var admin = await Admin();
        var used = await NewCategory(admin, "Scarves");
        var empty = await NewCategory(admin, "Gloves");
        await NewDraft(admin, used, "Wool Scarf");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/categories/{used}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/categories/{empty}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/categories", new { name = "Scarves" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Featured_products_must_be_published_and_keep_their_order()
    {
        var admin = await Admin();
        var category = await NewCategory(admin, "Hats");
        var first = await NewDraft(admin, category, "Beanie");
        var second = await NewDraft(admin, category, "Bucket Hat");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/featured", new { productIds = new[] { first } }, Ct)).StatusCode);

        foreach (var id in new[] { first, second })
        {
            await UploadImage(admin, id, Png);
            await admin.PostAsync($"/api/admin/products/{id}/publish", null, Ct);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/admin/featured", new { productIds = new[] { second, first } }, Ct)).StatusCode);
        var home = await factory.CreateApiClient().GetFromJsonAsync<HomeDto>("/api/home", Ct);
        Assert.Equal(["Bucket Hat", "Beanie"], home!.Featured.Select(f => f.Name));
    }

    private sealed record HomeDto(List<NameDto> Featured);
    private sealed record NameDto(string Name);

    [Fact]
    public async Task Changes_are_audited()
    {
        var admin = await Admin();
        var id = await NewDraft(admin, await NewCategory(admin, "Shorts"), "Gym Short");
        await UploadImage(admin, id, Png);
        await admin.PostAsync($"/api/admin/products/{id}/publish", null, Ct);
        var variant = (await Get(admin, id)).Variants[0].Id;
        await admin.PutAsJsonAsync($"/api/admin/products/{id}/variants/{variant}/stock", new { stockOnHand = 7 }, Ct);

        using var scope = factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync(Ct);

        Assert.Contains("product.created", actions);
        Assert.Contains("product.published", actions);
        Assert.Contains("variant.stock_set", actions);
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/products" },
        { "POST", "/api/admin/products" },
        { "PUT", $"/api/admin/products/{Guid.Empty}" },
        { "POST", $"/api/admin/products/{Guid.Empty}/publish" },
        { "DELETE", $"/api/admin/products/{Guid.Empty}" },
        { "PUT", $"/api/admin/products/{Guid.Empty}/options" },
        { "POST", $"/api/admin/products/{Guid.Empty}/variants" },
        { "PUT", $"/api/admin/products/{Guid.Empty}/variants/{Guid.Empty}/stock" },
        { "POST", $"/api/admin/products/{Guid.Empty}/images" },
        { "PUT", "/api/admin/featured" },
        { "POST", "/api/admin/categories" },
        { "DELETE", $"/api/admin/categories/{Guid.Empty}" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Admin_routes_refuse_customers_and_anonymous_visitors(string method, string url)
    {
        var customer = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Customer");
        var anonymous = factory.CreateApiClient();
        await anonymous.RefreshXsrfToken();

        // A body of the right kind, so routing reaches the real endpoint rather than the 404 catch-all.
        HttpRequestMessage Request() => new(new HttpMethod(method), url)
        {
            Content = method is "GET" or "DELETE" ? null
                : url.EndsWith("/images", StringComparison.Ordinal) ? new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "a.png" } }
                : JsonContent.Create(new { }),
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.SendAsync(Request(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(), Ct)).StatusCode);
    }
}
