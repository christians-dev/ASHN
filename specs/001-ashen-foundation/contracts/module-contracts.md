# Module Contracts (in-process)

Each `Ashen.<Module>.Contracts` project exposes **only** what other modules need. Everything
else in a module is `internal`. Signatures below are indicative; DTOs are `record`s, all methods
take a `CancellationToken` (omitted here for brevity).

A module may reference `Ashen.BuildingBlocks` and other modules' `.Contracts` projects only.
`Ashen.ArchitectureTests` enforces this plus "no public types outside Contracts".

## Catalog → used by Ordering, Customers, Reviews

```csharp
public interface ICatalogModule
{
    Task<IReadOnlyList<VariantForCheckout>> GetVariantsForCheckout(IReadOnlyCollection<Guid> variantIds);
    Task<IReadOnlyList<ProductSummary>> GetProductSummaries(IReadOnlyCollection<Guid> productIds);
    Task<Result> ReserveStock(Guid checkoutId, IReadOnlyCollection<StockLine> lines); // atomic; fails with shortages
    Task ConfirmStock(Guid checkoutId);
    Task ReleaseStock(Guid checkoutId);
    Task RestockForCancelledOrder(IReadOnlyCollection<StockLine> lines);
}
public record VariantForCheckout(Guid VariantId, Guid ProductId, string ProductName, string Sku,
    IReadOnlyDictionary<string,string> OptionValues, Money Price, int Available, bool IsPurchasable);
public record StockLine(Guid VariantId, int Quantity);
```

## Pricing → used by Ordering, Customers, Payments

```csharp
public interface IPricingModule
{
    Task<RegionInfo> GetActiveRegion();                       // currency, locale, shipping countries, states
    Task<PriceQuote> Quote(Money merchandiseAfterDiscount, string countryCode, string jurisdictionCode);
}
public record PriceQuote(Money Shipping, decimal TaxRate, string TaxJurisdiction, Money Tax);

// internal to Pricing — the swappable port (Principle III)
internal interface ITaxCalculator { Task<TaxResult> Calculate(Money taxableAmount, string countryCode, string jurisdictionCode); }
```

## Promotions → used by Ordering

```csharp
public interface IPromotionsModule
{
    Task<CouponCheck> Validate(string code, Guid? customerId, Money subtotal);   // discount or refusal reason
    Task<Result> ReserveCoupon(string code, Guid customerId, Guid checkoutId, Money subtotal);
    Task ConfirmCoupon(Guid checkoutId, Guid orderId);
    Task ReleaseCoupon(Guid checkoutId);
}
```

## Payments → used by Ordering

```csharp
public interface IPaymentsModule
{
    Task<PaymentAuthorization> Authorize(Guid checkoutId, Guid customerId, Money amount, PaymentSource source);
    Task Capture(Guid paymentId, Guid orderId);
    Task Void(Guid paymentId);
    Task Refund(Guid paymentId);
    Task<CardSummary?> GetCard(Guid customerId, Guid savedCardId);
}
public abstract record PaymentSource;
public record SavedCardSource(Guid SavedCardId) : PaymentSource;
public record NewCardSource(string Number, int ExpMonth, int ExpYear, string Cvc) : PaymentSource;
public record PaymentAuthorization(bool Approved, Guid? PaymentId, string? DeclineReason, CardSummary? Card);
```

The gateway port itself is in [payment-gateway.md](payment-gateway.md).

## Customers → used by Ordering

```csharp
public interface ICustomersModule
{
    Task<AddressSnapshot?> GetAddress(Guid customerId, Guid addressId);
}
```

## Ordering → used by Reviews

```csharp
public interface IOrderingModule
{
    Task<bool> HasDeliveredProduct(Guid customerId, Guid productId);
}
```

Catalog does not ask Ordering whether a product was ordered: `ConfirmStock` sets
`Product.HasBeenOrdered`, keeping Catalog free of an Ordering dependency (FR-044).

## Reviews → used by Catalog

```csharp
public interface IReviewsModule
{
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetRatings(IReadOnlyCollection<Guid> productIds);
}
public record RatingSummary(decimal Average, int Count);
```

## Identity → used by host and all modules

```csharp
public interface ICurrentUser { Guid? UserId { get; } bool IsAdmin { get; } string? DisplayName { get; } }
```

## Integration events (in-process `IEventBus`, published after commit)

| Event | Publisher | Handlers |
|---|---|---|
| `AccountDeletionRequested(UserId)` | Identity | Customers (delete profile), Payments (delete cards), Reviews (anonymize author), Ordering (strip personal data from orders) |
| `OrderPlaced(OrderId, CustomerId, Email)` | Ordering | Ordering (send confirmation email) |
| `OrderShipped(OrderId, CustomerId)` | Ordering | Ordering (send shipping email) |

Shortcut (R8): not persisted; upgrade to outbox when a module is extracted.
