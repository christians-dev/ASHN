using System.Net;
using System.Text;

using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Catalog.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ashen.Catalog.Data;

/// <summary>
/// Sample catalog for local development (quickstart.md), only when <c>Catalog:Seed:SampleData</c> is true.
/// Placeholder images are generated SVG tiles, so no binary files live in the repo.
/// </summary>
internal sealed class CatalogSeeder(CatalogDbContext db, IFileStorage storage, IConfiguration configuration, TimeProvider clock)
    : IModuleSeeder
{
    private sealed record SeedProduct(string Name, string Category, decimal Price, string[] Colours, int? Featured = null, bool Published = true, string? SoldOutSize = null);

    private static readonly string[] Sizes = ["S", "M", "L", "XL"];

    private static readonly (string Name, string Slug)[] Categories =
        [("Tops", "tops"), ("Hoodies", "hoodies"), ("Jackets", "jackets"), ("Pants", "pants"), ("Shoes", "shoes")];

    private static readonly SeedProduct[] Products =
    [
        new("Essential Tee", "Tops", 24m, ["White", "Black"], Featured: 1),
        new("Heavyweight Tee", "Tops", 32m, ["Ash", "Black"]),
        new("Long Sleeve Tee", "Tops", 36m, ["Black", "Olive"], SoldOutSize: "M"),
        new("Linen Shirt", "Tops", 58m, ["Sand", "White"]),
        new("Black Hoodie", "Hoodies", 64m, ["Black", "Charcoal"], Featured: 2),
        new("Zip Hoodie", "Hoodies", 72m, ["Ash", "Navy"]),
        new("Cropped Hoodie", "Hoodies", 60m, ["Bone", "Black"], SoldOutSize: "L"),
        new("Fleece Hoodie", "Hoodies", 78m, ["Forest", "Black"]),
        new("Field Jacket", "Jackets", 148m, ["Olive", "Black"], Featured: 3),
        new("Denim Jacket", "Jackets", 118m, ["Indigo", "Black"]),
        new("Puffer Jacket", "Jackets", 189m, ["Black", "Slate"]),
        new("Rain Shell", "Jackets", 132m, ["Yellow", "Black"], SoldOutSize: "S"),
        new("Relaxed Chino", "Pants", 68m, ["Khaki", "Black"]),
        new("Cargo Pant", "Pants", 82m, ["Olive", "Black"]),
        new("Track Pant", "Pants", 56m, ["Black", "Navy"]),
        new("Wide Leg Trouser", "Pants", 92m, ["Charcoal", "Sand"]),
        new("Canvas Sneaker", "Shoes", 74m, ["White", "Black"]),
        new("Leather Boot", "Shoes", 210m, ["Brown", "Black"]),
        new("Slide Sandal", "Shoes", 38m, ["Black", "Bone"], Published: false),
    ];

    public async Task Seed(CancellationToken cancellationToken)
    {
        await SeedSampleData(cancellationToken);
        await SeedLargeCatalog(configuration.GetValue<int>("Catalog:Seed:LargeCatalog"), cancellationToken);
    }

    /// <summary>
    /// <c>Catalog:Seed:LargeCatalog=1000</c> adds that many published products (8 variants each) for the SC-005/SC-006
    /// performance and load checks. Safe to re-run: it only tops up to the requested count.
    /// </summary>
    private async Task SeedLargeCatalog(int count, CancellationToken cancellationToken)
    {
        const string prefix = "load-test-";
        var existing = await db.Products.CountAsync(p => p.Slug.StartsWith(prefix), cancellationToken);
        if (count <= existing)
        {
            return;
        }

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Slug == "load-test", cancellationToken)
            ?? db.Categories.Add(new Category("Load test", "load-test", 99)).Entity;
        const string image = "seed/load-test.svg";
        await using (var svg = new MemoryStream(Encoding.UTF8.GetBytes(PlaceholderSvg("Load test"))))
        {
            await storage.Save(image, svg, cancellationToken);
        }

        string[] colours = ["Black", "Stone"];
        var now = clock.GetUtcNow();
        for (var i = existing; i < count; i++)
        {
            var name = $"Load Test Item {i + 1:0000}";
            var product = Product.Create(name, $"{prefix}{i + 1:0000}", $"Generated product {i + 1} for performance checks.", category.Id, now.AddSeconds(-i));
            product.DefineOptions([new ProductOption("Size", [.. Sizes]), new ProductOption("Colour", [.. colours])]);
            foreach (var colour in colours)
            {
                foreach (var size in Sizes)
                {
                    product.AddVariant($"LT{i + 1:0000}-{size}-{colour[0]}", new Dictionary<string, string> { ["Size"] = size, ["Colour"] = colour }, Usd(20m + (i % 180)), 25);
                }
            }

            product.AddImage(image, name);
            product.Publish(now);
            db.Products.Add(product);
            if ((i + 1) % 200 == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSampleData(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Catalog:Seed:SampleData") || await db.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var categories = Categories.Select((c, i) => new Category(c.Name, c.Slug, i)).ToList();
        db.Categories.AddRange(categories);

        var position = 0;
        foreach (var seed in Products)
        {
            var product = await NewProduct(seed.Name, categories.Single(c => c.Name == seed.Category).Id, now.AddMinutes(position++), cancellationToken);
            product.DefineOptions([new ProductOption("Size", [.. Sizes]), new ProductOption("Colour", [.. seed.Colours])]);
            foreach (var colour in seed.Colours)
            {
                foreach (var size in Sizes.Take(colour == seed.Colours[0] ? 4 : 2))
                {
                    var stock = size == seed.SoldOutSize ? 0 : 10;
                    product.AddVariant(Sku(seed.Name, size, colour), new Dictionary<string, string> { ["Size"] = size, ["Colour"] = colour }, Usd(seed.Price), stock);
                }
            }

            Finish(product, seed.Published, seed.Featured, now);
        }

        // A shoe with a third option: shows FR-005 and the 1–3 options rule.
        var runner = await NewProduct("Trail Runner", categories.Single(c => c.Name == "Shoes").Id, now.AddMinutes(position), cancellationToken);
        runner.DefineOptions([new ProductOption("Size", ["8", "9", "10", "11"]), new ProductOption("Colour", ["Grey"]), new ProductOption("Width", ["Regular", "Wide"])]);
        foreach (var size in new[] { "8", "9", "10", "11" })
        {
            foreach (var width in new[] { "Regular", "Wide" })
            {
                runner.AddVariant(Sku("Trail Runner", size, width), new Dictionary<string, string> { ["Size"] = size, ["Colour"] = "Grey", ["Width"] = width }, Usd(135m), width == "Wide" && size == "11" ? 0 : 6);
            }
        }

        Finish(runner, published: true, featured: 4, now);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Product> NewProduct(string name, Guid categoryId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var slug = Slug.From(name);
        var product = Product.Create(name, slug, $"The {name.ToLowerInvariant()}: a sample product for local development.", categoryId, at);

        var path = $"seed/{slug}.svg";
        await using var image = new MemoryStream(Encoding.UTF8.GetBytes(PlaceholderSvg(name)));
        await storage.Save(path, image, cancellationToken);
        product.AddImage(path, name);
        return product;
    }

    private void Finish(Product product, bool published, int? featured, DateTimeOffset now)
    {
        if (published)
        {
            product.Publish(product.CreatedAt);
        }

        product.SetFeatured(featured);
        db.Products.Add(product);
    }

    private static Money Usd(decimal amount) => new(amount, "USD");

    private static string Sku(string name, string a, string b) =>
        $"{string.Concat(name.Split(' ').Select(w => w[0]))}-{a}-{b}".ToUpperInvariant();

    private static string PlaceholderSvg(string name) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="600" height="750" viewBox="0 0 600 750"><rect width="600" height="750" fill="#3a3a3a"/><text x="300" y="375" fill="#f2f2f2" font-family="sans-serif" font-size="40" text-anchor="middle">{WebUtility.HtmlEncode(name)}</text></svg>""";
}
