using Ashen.BuildingBlocks;

namespace Ashen.Catalog.Contracts;

/// <summary>What other modules may ask Catalog (module-contracts.md).</summary>
public interface ICatalogModule
{
    Task<IReadOnlyList<ProductSummary>> GetProductSummaries(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);

    /// <summary>Current price, availability and display details for cart and checkout lines.</summary>
    Task<IReadOnlyList<VariantForCheckout>> GetVariantsForCheckout(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Holds stock for a checkout, all lines or none; repeats for the same checkout succeed without holding twice.</summary>
    Task<Result> ReserveStock(Guid checkoutId, IReadOnlyCollection<StockLine> lines, CancellationToken cancellationToken = default);

    /// <summary>The order is saved: held stock leaves the shelf for good.</summary>
    Task ConfirmStock(Guid checkoutId, CancellationToken cancellationToken = default);

    /// <summary>Checkout failed: held stock goes back on sale.</summary>
    Task ReleaseStock(Guid checkoutId, CancellationToken cancellationToken = default);

    /// <summary>A cancelled order's items go back on sale (FR-036).</summary>
    Task RestockForCancelledOrder(IReadOnlyCollection<StockLine> lines, CancellationToken cancellationToken = default);
}

/// <summary>
/// A product as shown in lists: name, picture, lowest price, whether anything is in stock, and its average rating
/// from visible reviews (null with no reviews).
/// </summary>
public sealed record ProductSummary(
    Guid ProductId,
    string Name,
    string Slug,
    string? ImageUrl,
    Money? FromPrice,
    bool InStock,
    bool IsPublished,
    decimal? RatingAverage,
    int RatingCount);

/// <summary>
/// One variant as checkout sees it. <see cref="IsPurchasable"/> is false when the product isn't published
/// or nothing is in stock.
/// </summary>
public sealed record VariantForCheckout(
    Guid VariantId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ImageUrl,
    string Sku,
    IReadOnlyDictionary<string, string> OptionValues,
    Money Price,
    int Available,
    bool IsPurchasable);

public sealed record StockLine(Guid VariantId, int Quantity);
