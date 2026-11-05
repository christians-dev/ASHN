using Ashen.BuildingBlocks;
using Ashen.Catalog.Contracts;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Catalog.Shared;
using Ashen.Reviews.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ashen.Catalog;

internal sealed partial class CatalogModuleApi(
    CatalogDbContext db, IFileStorage storage, IReviewsModule reviews, TimeProvider clock, ILogger<CatalogModuleApi> logger) : ICatalogModule
{
    public async Task<IReadOnlyList<ProductSummary>> GetProductSummaries(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
        await db.Products.Where(p => productIds.Contains(p.Id)).ToSummaries(storage, reviews, cancellationToken);

    public async Task<IReadOnlyList<VariantForCheckout>> GetVariantsForCheckout(
        IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default)
    {
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Where(p => p.Variants.Any(v => variantIds.Contains(v.Id)))
            .ToListAsync(cancellationToken);

        return products
            .SelectMany(p => p.Variants.Where(v => variantIds.Contains(v.Id)).Select(v => new VariantForCheckout(
                v.Id,
                p.Id,
                p.Name,
                p.Slug,
                p.Images.OrderBy(i => i.SortOrder).Select(i => storage.PublicUrl(i.StoragePath)).FirstOrDefault(),
                v.Sku,
                v.OptionValues.ToDictionary(o => o.Name, o => o.Value),
                v.Price,
                Math.Max(0, v.Available),
                p.Status == ProductStatus.Published && v.IsAvailable)))
            .ToList();
    }

    public async Task<Result> ReserveStock(Guid checkoutId, IReadOnlyCollection<StockLine> lines, CancellationToken cancellationToken = default)
    {
        if (await db.Set<StockReservation>().AnyAsync(r => r.CheckoutId == checkoutId && r.Status == ReservationStatus.Pending, cancellationToken))
        {
            return Result.Success(); // already held for this checkout
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var line in lines)
        {
            // One conditional UPDATE per line: the row lock makes "check stock, then take it" a single step,
            // so two shoppers can never both take the last unit (SC-007).
            var held = await db.Variants
                .Where(v => v.Id == line.VariantId && v.StockOnHand - v.StockReserved >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockReserved, v => v.StockReserved + line.Quantity), cancellationToken);

            if (held == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear(); // drop reservations queued for earlier lines; nothing was held
                var available = await db.Variants.Where(v => v.Id == line.VariantId)
                    .Select(v => (int?)(v.StockOnHand - v.StockReserved)).SingleOrDefaultAsync(cancellationToken) ?? 0;
                return Error.Conflict("stock.insufficient", available > 0
                    ? $"Only {available} of an item in your cart are left."
                    : "An item in your cart has just sold out.");
            }

            db.Set<StockReservation>().Add(new StockReservation(checkoutId, line.VariantId, line.Quantity, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task ConfirmStock(Guid checkoutId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var reservation in await Pending(checkoutId, cancellationToken))
        {
            await db.Variants.Where(v => v.Id == reservation.VariantId).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockOnHand, v => v.StockOnHand - reservation.Quantity)
                .SetProperty(v => v.StockReserved, v => v.StockReserved - reservation.Quantity), cancellationToken);
            await db.Products.Where(p => p.Variants.Any(v => v.Id == reservation.VariantId))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.HasBeenOrdered, true), cancellationToken); // FR-044
            reservation.Confirm();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReleaseStock(Guid checkoutId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var reservation in await Pending(checkoutId, cancellationToken))
        {
            await db.Variants.Where(v => v.Id == reservation.VariantId).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockReserved, v => v.StockReserved - reservation.Quantity), cancellationToken);
            reservation.Release();
            LogReleased(checkoutId, reservation.VariantId, reservation.Quantity);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RestockForCancelledOrder(IReadOnlyCollection<StockLine> lines, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var line in lines)
        {
            await db.Variants.Where(v => v.Id == line.VariantId).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockOnHand, v => v.StockOnHand + line.Quantity), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // FOR UPDATE: a confirm and a release of the same checkout (e.g. the expiry job) queue on these rows, and the
    // second re-checks the status after the first commits, so only one of them changes the stock counters.
    private Task<List<StockReservation>> Pending(Guid checkoutId, CancellationToken cancellationToken) =>
        db.Set<StockReservation>()
            .FromSql($"SELECT * FROM catalog.stock_reservations WHERE checkout_id = {checkoutId} AND status = 'Pending' FOR UPDATE")
            .ToListAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Quantity} of variant {VariantId} held for checkout {CheckoutId}")]
    private partial void LogReleased(Guid checkoutId, Guid variantId, int quantity);
}
