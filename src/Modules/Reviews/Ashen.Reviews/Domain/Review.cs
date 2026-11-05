using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Domain;

namespace Ashen.Reviews.Domain;

/// <summary>
/// A verified buyer's review of a product (FR-039): 1-5 stars and optional text. One per customer per product.
/// Hidden reviews stay stored but are not shown or counted in the average (FR-040).
/// </summary>
internal sealed class Review : AggregateRoot
{
    public const int MaxTextLength = 2000;
    public const string FormerCustomer = "Former customer";

    private Review() { }

    public static Result<Review> Write(Guid productId, Guid customerId, string authorDisplayName, int rating, string? text, DateTimeOffset now)
    {
        var review = new Review
        {
            ProductId = productId,
            CustomerId = customerId,
            AuthorDisplayName = authorDisplayName,
            CreatedAt = now,
        };
        var edited = review.Edit(rating, text, now);
        return edited.IsSuccess ? review : edited.Error!;
    }

    public Guid ProductId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string AuthorDisplayName { get; private set; } = "";
    public int Rating { get; private set; }
    public string? Text { get; private set; }
    public bool IsHidden { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Result Edit(int rating, string? text, DateTimeOffset now)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var fields = new Dictionary<string, string[]>();
        if (rating is < 1 or > 5)
        {
            fields["rating"] = ["Choose 1 to 5 stars."];
        }

        if (trimmed?.Length > MaxTextLength)
        {
            fields["text"] = [$"Keep your review to {MaxTextLength:N0} characters."];
        }

        if (fields.Count > 0)
        {
            return Error.Validation("review.invalid", "Please fix the highlighted fields.", fields);
        }

        Rating = rating;
        Text = trimmed;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Hide() => IsHidden = true;

    public void Unhide() => IsHidden = false;

    /// <summary>The author deleted their account: the review stays, without their name.</summary>
    public void Anonymize() => AuthorDisplayName = FormerCustomer;

    /// <summary>"Sam Lee" → "Sam L.": enough to feel personal without publishing a full name.</summary>
    public static string PublicName(string? displayName)
    {
        var parts = (displayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Customer",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
