using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.IntegrationTests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Catalog;

/// <summary>A small known catalog: three published products across three categories, plus one draft.</summary>
public sealed class CatalogFactory : AshenApiFactory
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var hoodies = new Category("Hoodies", "hoodies", 0);
        var jackets = new Category("Jackets", "jackets", 1);
        var shoes = new Category("Shoes", "shoes", 2);
        db.Categories.AddRange(hoodies, jackets, shoes);

        var blackHoodie = Product.Create("Black Hoodie", "black-hoodie", "A warm hoodie.", hoodies.Id, T0);
        blackHoodie.DefineOptions([new("Size", ["S", "M", "L"]), new("Colour", ["Black", "Grey"])]);
        blackHoodie.AddVariant("BH-S-BLK", Values(("Size", "S"), ("Colour", "Black")), Usd(50m), 5);
        blackHoodie.AddVariant("BH-M-BLK", Values(("Size", "M"), ("Colour", "Black")), Usd(50m), 0);
        blackHoodie.AddVariant("BH-L-GRY", Values(("Size", "L"), ("Colour", "Grey")), Usd(55m), 2);
        PublishWithImage(blackHoodie, T0.AddDays(1));
        blackHoodie.SetFeatured(2);

        var rainJacket = Product.Create("Rain Jacket", "rain-jacket", "Keeps you dry.", jackets.Id, T0);
        rainJacket.DefineOptions([new("Size", ["M", "L"]), new("Colour", ["Yellow"])]);
        rainJacket.AddVariant("RJ-M-YEL", Values(("Size", "M"), ("Colour", "Yellow")), Usd(120m), 3);
        PublishWithImage(rainJacket, T0.AddDays(2));
        rainJacket.SetFeatured(1);

        var trailRunner = Product.Create("Trail Runner", "trail-runner", "A running shoe.", shoes.Id, T0);
        trailRunner.DefineOptions([new("Size", ["9", "10"]), new("Colour", ["Grey"]), new("Width", ["Regular", "Wide"])]);
        trailRunner.AddVariant("TR-9-WIDE", Values(("Size", "9"), ("Colour", "Grey"), ("Width", "Wide")), Usd(135m), 4);
        PublishWithImage(trailRunner, T0.AddDays(3));

        var draft = Product.Create("Secret Hoodie", "secret-hoodie", "Not released yet.", hoodies.Id, T0);
        draft.DefineOptions([new("Size", ["M"])]);
        draft.AddVariant("SH-M", Values(("Size", "M")), Usd(70m), 9);
        draft.AddImage("seed/secret.svg", "Secret hoodie");

        db.Products.AddRange(blackHoodie, rainJacket, trailRunner, draft);
        await db.SaveChangesAsync();
    }

    private static void PublishWithImage(Product product, DateTimeOffset at)
    {
        product.AddImage($"seed/{product.Slug}.svg", product.Name);
        Assert.True(product.Publish(at).IsSuccess);
    }

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    private static Money Usd(decimal amount) => new(amount, "USD");
}

public class BrowseProductsTests(CatalogFactory factory) : IClassFixture<CatalogFactory>
{
    private sealed record MoneyDto(decimal Amount, string Currency);
    private sealed record Summary(string Name, string Slug, string? ImageUrl, MoneyDto? FromPrice, bool InStock);
    private sealed record Page(List<Summary> Items, int TotalCount);
    private sealed record Category(string Name, string Slug);
    private sealed record Home(List<Summary> Featured, List<Category> Categories);
    private sealed record Option(string Name, List<string> Values);
    private sealed record Variant(string Sku, Dictionary<string, string> OptionValues, MoneyDto Price, bool IsAvailable);
    private sealed record Detail(string Name, string CategorySlug, List<Option> Options, List<Variant> Variants);

    private async Task<List<string>> Names(string query)
    {
        var page = await factory.CreateApiClient().GetFromJsonAsync<Page>($"/api/products{query}", TestContext.Current.CancellationToken);
        return page!.Items.Select(i => i.Name).ToList();
    }

    [Fact]
    public async Task Lists_published_products_newest_first_by_default()
    {
        Assert.Equal(["Trail Runner", "Rain Jacket", "Black Hoodie"], await Names(""));
    }

    [Fact]
    public async Task Filters_by_category_and_never_shows_drafts()
    {
        Assert.Equal(["Black Hoodie"], await Names("?category=hoodies"));
    }

    [Fact]
    public async Task Filters_by_size_and_colour_ignoring_case()
    {
        Assert.Equal(["Rain Jacket", "Black Hoodie"], await Names("?size=m"));
        Assert.Equal(["Trail Runner", "Black Hoodie"], await Names("?colour=GREY"));
    }

    [Fact]
    public async Task Size_and_colour_must_match_the_same_variant()
    {
        // The hoodie has M in Black and L in Grey, but no M in Grey.
        Assert.Empty(await Names("?size=m&colour=grey"));
    }

    [Fact]
    public async Task Filters_by_price_range()
    {
        Assert.Equal(["Trail Runner", "Rain Jacket"], await Names("?minPrice=100"));
        Assert.Equal(["Black Hoodie"], await Names("?maxPrice=60"));
    }

    [Fact]
    public async Task Sorts_by_price()
    {
        Assert.Equal(["Black Hoodie", "Rain Jacket", "Trail Runner"], await Names("?sort=price_asc"));
        Assert.Equal(["Trail Runner", "Rain Jacket", "Black Hoodie"], await Names("?sort=price_desc"));
    }

    [Fact]
    public async Task Searches_name_and_description()
    {
        Assert.Equal(["Black Hoodie"], await Names("?q=hoodie"));
        Assert.Equal(["Rain Jacket"], await Names("?q=DRY"));
        Assert.Empty(await Names("?q=%25"));
    }

    [Fact]
    public async Task Summary_shows_lowest_price_image_and_stock()
    {
        var page = await factory.CreateApiClient().GetFromJsonAsync<Page>("/api/products?category=hoodies", TestContext.Current.CancellationToken);
        var hoodie = Assert.Single(page!.Items);

        Assert.Equal(new MoneyDto(50m, "USD"), hoodie.FromPrice);
        Assert.Equal("/media/seed/black-hoodie.svg", hoodie.ImageUrl);
        Assert.True(hoodie.InStock);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Detail_returns_all_options_including_non_standard_ones()
    {
        var detail = await factory.CreateApiClient().GetFromJsonAsync<Detail>("/api/products/trail-runner", TestContext.Current.CancellationToken);

        Assert.Equal(["Size", "Colour", "Width"], detail!.Options.Select(o => o.Name));
        Assert.Equal("Wide", Assert.Single(detail.Variants).OptionValues["Width"]);
        Assert.Equal("shoes", detail.CategorySlug);
    }

    [Fact]
    public async Task Detail_marks_out_of_stock_variants_unavailable()
    {
        var detail = await factory.CreateApiClient().GetFromJsonAsync<Detail>("/api/products/black-hoodie", TestContext.Current.CancellationToken);

        Assert.False(detail!.Variants.Single(v => v.Sku == "BH-M-BLK").IsAvailable);
        Assert.True(detail.Variants.Single(v => v.Sku == "BH-S-BLK").IsAvailable);
    }

    [Theory]
    [InlineData("secret-hoodie")]
    [InlineData("does-not-exist")]
    public async Task Unpublished_or_unknown_product_is_not_found(string slug)
    {
        var response = await factory.CreateApiClient().GetAsync($"/api/products/{slug}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Home_shows_featured_products_in_order_and_categories()
    {
        var home = await factory.CreateApiClient().GetFromJsonAsync<Home>("/api/home", TestContext.Current.CancellationToken);

        Assert.Equal(["Rain Jacket", "Black Hoodie"], home!.Featured.Select(f => f.Name));
        Assert.Equal(["hoodies", "jackets", "shoes"], home.Categories.Select(c => c.Slug));
    }
}
