using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Catalog.Domain;

internal enum ProductStatus
{
    Draft,
    Published,
    Unpublished,
    Archived,
}

/// <summary>A named option a product's variants choose a value for, e.g. Size with S, M, L.</summary>
internal sealed record ProductOption(string Name, List<string> Values);

/// <summary>One option value on a variant, e.g. Size = M. Stored as JSON on the variant.</summary>
internal sealed record OptionValue(string Name, string Value);

/// <summary>
/// A product and everything sold under it. All changes to variants, images and options go through here,
/// so the option rules in data-model.md always hold (constitution V).
/// </summary>
internal sealed class Product : AggregateRoot
{
    public const int MaxOptions = 3;
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    private readonly List<Variant> _variants = [];
    private readonly List<ProductImage> _images = [];

    private Product() { }

    public static Product Create(string name, string slug, string description, Guid categoryId, DateTimeOffset now) =>
        new()
        {
            Name = name,
            Slug = slug,
            Description = description,
            CategoryId = categoryId,
            Status = ProductStatus.Draft,
            CreatedAt = now,
        };

    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string Description { get; private set; } = "";
    public Guid CategoryId { get; private set; }
    public ProductStatus Status { get; private set; }
    public bool IsFeatured { get; private set; }
    public int? FeaturedPosition { get; private set; }
    public bool HasBeenOrdered { get; private set; }
    public List<ProductOption> Options { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyList<Variant> Variants => _variants;
    public IReadOnlyList<ProductImage> Images => _images;

    /// <summary>Replaces the option definitions. Once variants exist only values may change, and used values must stay.</summary>
    public Result DefineOptions(IReadOnlyList<ProductOption> options)
    {
        if (options.Count is < 1 or > MaxOptions)
        {
            return Error.Validation("options.count", $"A product needs 1 to {MaxOptions} options.");
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o.Name) || o.Values.Count == 0 || o.Values.Any(string.IsNullOrWhiteSpace)))
        {
            return Error.Validation("options.empty", "Every option needs a name and at least one value.");
        }

        if (options.DistinctBy(o => o.Name.ToUpperInvariant()).Count() != options.Count
            || options.Any(o => o.Values.DistinctBy(v => v.ToUpperInvariant()).Count() != o.Values.Count))
        {
            return Error.Validation("options.duplicate", "Option names, and values within an option, must be unique.");
        }

        if (_variants.Count > 0)
        {
            var sameNames = options.Count == Options.Count && options.All(o => Options.Any(e => e.Name.Equals(o.Name, IgnoreCase)));
            if (!sameNames)
            {
                return Error.Conflict("options.locked", "Options can only be added or removed while the product has no variants.");
            }

            var removedButUsed = _variants.SelectMany(v => v.OptionValues).FirstOrDefault(used =>
                !options.Single(o => o.Name.Equals(used.Name, IgnoreCase)).Values.Contains(used.Value, StringComparer.OrdinalIgnoreCase));
            if (removedButUsed is not null)
            {
                return Error.Conflict("options.value_in_use", $"{removedButUsed.Name} \"{removedButUsed.Value}\" is used by a variant.");
            }
        }

        Options = options.Select(o => new ProductOption(o.Name.Trim(), o.Values.Select(v => v.Trim()).ToList())).ToList();
        return Result.Success();
    }

    public Result AddOptionValue(string optionName, string value)
    {
        var option = Options.SingleOrDefault(o => o.Name.Equals(optionName, IgnoreCase));
        if (option is null)
        {
            return Error.NotFound("options.not_found", $"This product has no option \"{optionName}\".");
        }

        if (!option.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            // Replace the list so EF Core sees the JSON column change.
            Options = Options.Select(o => o == option ? o with { Values = [.. o.Values, value.Trim()] } : o).ToList();
        }

        return Result.Success();
    }

    public Result<Variant> AddVariant(string sku, IReadOnlyDictionary<string, string> optionValues, Money price, int stockOnHand)
    {
        var values = ResolveOptionValues(optionValues, exceptVariant: null);
        if (!values.IsSuccess)
        {
            return values.Error!;
        }

        if (price.IsNegative || stockOnHand < 0)
        {
            return Error.Validation("variant.invalid", "Price and stock cannot be negative.");
        }

        var variant = new Variant(sku, values.Value, price, stockOnHand);
        _variants.Add(variant);
        return variant;
    }

    public Result UpdateVariant(Guid variantId, IReadOnlyDictionary<string, string> optionValues, Money price)
    {
        var variant = _variants.SingleOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Error.NotFound("variant.not_found", "Variant not found.");
        }

        var values = ResolveOptionValues(optionValues, exceptVariant: variant);
        if (!values.IsSuccess)
        {
            return values.Error!;
        }

        if (price.IsNegative)
        {
            return Error.Validation("variant.invalid", "Price cannot be negative.");
        }

        variant.Update(values.Value, price);
        return Result.Success();
    }

    /// <summary>Name, description and category. The slug stays the same so existing links keep working.</summary>
    public void UpdateDetails(string name, string description, Guid categoryId)
    {
        Name = name;
        Description = description;
        CategoryId = categoryId;
    }

    public Result SetStock(Guid variantId, int stockOnHand)
    {
        var variant = _variants.SingleOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Error.NotFound("variant.not_found", "Variant not found.");
        }

        if (stockOnHand < 0)
        {
            return Error.Validation("variant.stock", "Stock cannot be negative.");
        }

        variant.SetStock(stockOnHand);
        return Result.Success();
    }

    public ProductImage AddImage(string storagePath, string altText)
    {
        var image = new ProductImage(storagePath, altText, _images.Count);
        _images.Add(image);
        return image;
    }

    /// <returns>The removed image, so its file can be deleted, or null if it isn't on this product.</returns>
    public ProductImage? RemoveImage(Guid imageId)
    {
        var image = _images.SingleOrDefault(i => i.Id == imageId);
        if (image is not null)
        {
            _images.Remove(image);
            ReorderImages(_images.OrderBy(i => i.SortOrder).Select(i => i.Id).ToList());
        }

        return image;
    }

    /// <summary>Sets the display order; <paramref name="imageIds"/> must list every image exactly once.</summary>
    public Result ReorderImages(IReadOnlyList<Guid> imageIds)
    {
        if (imageIds.Count != _images.Count || imageIds.Except(_images.Select(i => i.Id)).Any())
        {
            return Error.Validation("images.order", "List every image of the product exactly once.");
        }

        foreach (var image in _images)
        {
            image.MoveTo(imageIds.ToList().IndexOf(image.Id));
        }

        return Result.Success();
    }

    public Result Publish(DateTimeOffset now)
    {
        if (Status == ProductStatus.Archived)
        {
            return Error.Conflict("product.archived", "An archived product cannot be published.");
        }

        if (_images.Count == 0 || _variants.Count == 0)
        {
            return Error.Validation("product.incomplete", "A product needs at least one image and one variant before publishing.");
        }

        Status = ProductStatus.Published;
        PublishedAt ??= now;
        return Result.Success();
    }

    public Result Unpublish()
    {
        if (Status != ProductStatus.Published)
        {
            return Error.Conflict("product.not_published", "Only a published product can be unpublished.");
        }

        Status = ProductStatus.Unpublished;
        return Result.Success();
    }

    public void Archive()
    {
        Status = ProductStatus.Archived;
        SetFeatured(null);
    }

    /// <summary>Puts the product on the home page at <paramref name="position"/>, or removes it with null.</summary>
    public void SetFeatured(int? position)
    {
        IsFeatured = position is not null;
        FeaturedPosition = position;
    }

    public void MarkOrdered() => HasBeenOrdered = true;

    /// <summary>Maps loosely-cased input to the product's own option names and values, one per option.</summary>
    private Result<List<OptionValue>> ResolveOptionValues(IReadOnlyDictionary<string, string> input, Variant? exceptVariant)
    {
        if (input.Count != Options.Count)
        {
            return Error.Validation("variant.options", $"Choose exactly one value for each option: {string.Join(", ", Options.Select(o => o.Name))}.");
        }

        var resolved = new List<OptionValue>();
        foreach (var option in Options)
        {
            var given = input.FirstOrDefault(kv => kv.Key.Equals(option.Name, IgnoreCase));
            var value = given.Value is null ? null : option.Values.FirstOrDefault(v => v.Equals(given.Value.Trim(), IgnoreCase));
            if (value is null)
            {
                return Error.Validation("variant.options", $"\"{given.Value}\" is not an allowed {option.Name}.");
            }

            resolved.Add(new OptionValue(option.Name, value));
        }

        var key = Variant.KeyFor(resolved);
        if (_variants.Any(v => v != exceptVariant && v.OptionKey == key))
        {
            return Error.Conflict("variant.duplicate", "A variant with these options already exists.");
        }

        return resolved;
    }
}

/// <summary>One buyable version of a product, e.g. Size M / Colour Black.</summary>
internal sealed class Variant : Entity
{
    private Variant() { }

    internal Variant(string sku, List<OptionValue> optionValues, Money price, int stockOnHand)
    {
        Sku = sku;
        Price = price;
        StockOnHand = stockOnHand;
        Update(optionValues, price);
    }

    public string Sku { get; private set; } = "";
    public List<OptionValue> OptionValues { get; private set; } = [];

    /// <summary>Normalised option combination, e.g. <c>size=m;colour=black</c>; unique per product.</summary>
    public string OptionKey { get; private set; } = "";

    public Money Price { get; private set; }
    public int StockOnHand { get; private set; }
    public int StockReserved { get; private set; }

    public int Available => StockOnHand - StockReserved;
    public bool IsAvailable => Available > 0;

    public void SetStock(int stockOnHand) => StockOnHand = stockOnHand;

    internal void Update(List<OptionValue> optionValues, Money price)
    {
        OptionValues = optionValues;
        OptionKey = KeyFor(optionValues);
        Price = price;
    }

    internal static string KeyFor(IEnumerable<OptionValue> values) =>
        string.Join(';', values.Select(v => $"{v.Name.ToLowerInvariant()}={v.Value.ToLowerInvariant()}"));
}

internal sealed class ProductImage : Entity
{
    private ProductImage() { }

    internal ProductImage(string storagePath, string altText, int sortOrder)
    {
        StoragePath = storagePath;
        AltText = altText;
        SortOrder = sortOrder;
    }

    public string StoragePath { get; private set; } = "";
    public string AltText { get; private set; } = "";
    public int SortOrder { get; private set; }

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
