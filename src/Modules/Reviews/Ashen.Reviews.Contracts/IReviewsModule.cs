namespace Ashen.Reviews.Contracts;

/// <summary>What other modules may ask Reviews (module-contracts.md).</summary>
public interface IReviewsModule
{
    /// <summary>Average and count of visible reviews per product; products without reviews are left out.</summary>
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetRatings(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}

/// <param name="Average">Rounded to one decimal place, e.g. 4.3.</param>
public sealed record RatingSummary(decimal Average, int Count);
