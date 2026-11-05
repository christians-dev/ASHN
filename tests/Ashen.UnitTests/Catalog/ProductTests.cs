using Ashen.BuildingBlocks;
using Ashen.Catalog.Domain;

namespace Ashen.UnitTests.Catalog;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static Money Usd(decimal amount) => new(amount, "USD");

    private static Product NewProduct(params ProductOption[] options)
    {
        var product = Product.Create("Black Hoodie", "black-hoodie", "Warm.", Guid.CreateVersion7(), Now);
        Assert.True(product.DefineOptions(options.Length > 0 ? options : [Size("S", "M", "L"), Colour("Black")]).IsSuccess);
        return product;
    }

    private static ProductOption Size(params string[] values) => new("Size", [.. values]);
    private static ProductOption Colour(params string[] values) => new("Colour", [.. values]);
    private static Dictionary<string, string> Values(string size, string colour = "Black") => new() { ["Size"] = size, ["Colour"] = colour };

    [Fact]
    public void New_product_is_a_draft()
    {
        Assert.Equal(ProductStatus.Draft, NewProduct().Status);
    }

    [Fact]
    public void Publishing_requires_an_image_and_a_variant()
    {
        var product = NewProduct();
        Assert.False(product.Publish(Now).IsSuccess);

        product.AddVariant("HD-S", Values("S"), Usd(50m), 3);
        Assert.False(product.Publish(Now).IsSuccess);

        product.AddImage("seed/hoodie.svg", "Black hoodie");
        Assert.True(product.Publish(Now).IsSuccess);
        Assert.Equal(ProductStatus.Published, product.Status);
        Assert.Equal(Now, product.PublishedAt);
    }

    [Fact]
    public void Status_transitions_follow_the_lifecycle()
    {
        var product = NewProduct();
        Assert.False(product.Unpublish().IsSuccess);

        product.AddVariant("HD-S", Values("S"), Usd(50m), 3);
        product.AddImage("seed/hoodie.svg", "Black hoodie");
        product.Publish(Now);
        Assert.True(product.Unpublish().IsSuccess);
        Assert.Equal(ProductStatus.Unpublished, product.Status);
        Assert.True(product.Publish(Now).IsSuccess);

        product.Archive();
        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.False(product.Publish(Now).IsSuccess);
    }

    [Fact]
    public void Variant_availability_is_stock_on_hand_minus_reserved()
    {
        var product = NewProduct();
        var inStock = product.AddVariant("HD-S", Values("S"), Usd(50m), 2).Value;
        var soldOut = product.AddVariant("HD-M", Values("M"), Usd(50m), 0).Value;

        Assert.Equal(2, inStock.Available);
        Assert.True(inStock.IsAvailable);
        Assert.False(soldOut.IsAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Needs_one_to_three_options(int count)
    {
        var product = Product.Create("Hat", "hat", "", Guid.CreateVersion7(), Now);
        var options = Enumerable.Range(1, count).Select(i => new ProductOption($"Option{i}", ["A"])).ToArray();

        Assert.False(product.DefineOptions(options).IsSuccess);
    }

    [Fact]
    public void Option_names_are_unique_ignoring_case()
    {
        var product = Product.Create("Hat", "hat", "", Guid.CreateVersion7(), Now);

        Assert.False(product.DefineOptions([Size("S"), new ProductOption("size", ["M"])]).IsSuccess);
    }

    [Fact]
    public void Variant_needs_exactly_one_allowed_value_per_option()
    {
        var product = NewProduct();

        Assert.False(product.AddVariant("X1", new Dictionary<string, string> { ["Size"] = "S" }, Usd(1m), 1).IsSuccess);
        Assert.False(product.AddVariant("X2", Values("XXL"), Usd(1m), 1).IsSuccess);
        Assert.False(product.AddVariant("X3", new Dictionary<string, string> { ["Size"] = "S", ["Colour"] = "Black", ["Width"] = "Wide" }, Usd(1m), 1).IsSuccess);
        Assert.True(product.AddVariant("X4", new Dictionary<string, string> { ["size"] = "s", ["colour"] = "black" }, Usd(1m), 1).IsSuccess);
    }

    [Fact]
    public void Duplicate_option_combination_is_rejected()
    {
        var product = NewProduct();
        product.AddVariant("HD-S", Values("S"), Usd(50m), 1);

        var duplicate = product.AddVariant("HD-S-2", Values("s", "BLACK"), Usd(50m), 1);

        Assert.Equal(ErrorKind.Conflict, duplicate.Error?.Kind);
    }

    [Fact]
    public void Options_cannot_be_added_or_removed_once_variants_exist()
    {
        var product = NewProduct();
        product.AddVariant("HD-S", Values("S"), Usd(50m), 1);

        Assert.False(product.DefineOptions([Size("S", "M", "L"), Colour("Black"), new ProductOption("Width", ["Wide"])]).IsSuccess);
        Assert.False(product.DefineOptions([Size("S", "M", "L")]).IsSuccess);
    }

    [Fact]
    public void Values_can_be_added_but_a_used_value_cannot_be_removed()
    {
        var product = NewProduct();
        product.AddVariant("HD-S", Values("S"), Usd(50m), 1);

        Assert.True(product.AddOptionValue("Size", "XL").IsSuccess);
        Assert.Contains("XL", product.Options.Single(o => o.Name == "Size").Values);
        Assert.True(product.DefineOptions([Size("S", "M"), Colour("Black")]).IsSuccess); // L and XL unused
        Assert.False(product.DefineOptions([Size("M"), Colour("Black")]).IsSuccess);     // S is used
    }

    [Fact]
    public void Option_key_is_normalised_in_option_order()
    {
        var product = NewProduct();
        var variant = product.AddVariant("HD-S", new Dictionary<string, string> { ["colour"] = "Black", ["SIZE"] = "S" }, Usd(50m), 1).Value;

        Assert.Equal("size=s;colour=black", variant.OptionKey);
        Assert.Equal("S", variant.OptionValues.Single(v => v.Name == "Size").Value);
    }
}
