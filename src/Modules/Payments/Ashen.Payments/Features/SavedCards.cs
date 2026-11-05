using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;
using Ashen.Payments.Gateway;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Payments.Features;

/// <summary>
/// The signed-in customer's saved cards (FR-027, FR-029). The number and CVC go to the gateway once to be
/// tokenised and verified, then are dropped: only the token, brand, last four and expiry are stored.
/// </summary>
internal static class SavedCards
{
    public sealed record SaveCardRequest(string Number, int ExpMonth, int ExpYear, string Cvc)
    {
        // Keep the number and CVC out of logs and exception messages.
        public override string ToString() => "SaveCardRequest { ** redacted ** }";
    }

    public sealed record SavedCardDto(Guid Id, string Brand, string Last4, int ExpMonth, int ExpYear, bool IsDefault, bool IsExpired);

    internal sealed record Ctx(PaymentsDbContext Db, ICurrentUser User, TimeProvider Clock);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var cards = endpoints.MapGroup("/me/cards").WithTags("Payment methods");
        cards.MapGet("", List).Produces<List<SavedCardDto>>();
        cards.MapPost("", Save).Produces<SavedCardDto>(StatusCodes.Status201Created);
        cards.MapPost("/{id:guid}/default", SetDefault);
        cards.MapDelete("/{id:guid}", Delete);
    }

    private static Task<List<SavedCard>> Mine(Ctx c, CancellationToken ct) =>
        c.Db.SavedCards.Where(card => card.CustomerId == c.User.UserId).OrderBy(card => card.CreatedAt).ToListAsync(ct);

    private static SavedCardDto ToDto(SavedCard card, DateTimeOffset now) =>
        new(card.Id, card.Brand, card.Last4, card.ExpMonth, card.ExpYear, card.IsDefault, card.IsExpired(now));

    private static IResult NotFound() => Error.NotFound("card.not_found", "Card not found.").ToProblem();

    private static async Task<IResult> List([AsParameters] Ctx c, CancellationToken ct)
    {
        var now = c.Clock.GetUtcNow();
        return TypedResults.Ok((await Mine(c, ct)).OrderByDescending(card => card.IsDefault).Select(card => ToDto(card, now)).ToList());
    }

    private static async Task<IResult> Save(SaveCardRequest request, [AsParameters] Ctx c, IPaymentGateway gateway, CancellationToken ct)
    {
        var tokenized = await gateway.SaveCard(new CardDetails(request.Number ?? "", request.ExpMonth, request.ExpYear, request.Cvc ?? ""));
        // A zero-amount authorisation is the usual card check: a card the issuer would decline is not worth saving.
        var verified = tokenized.Succeeded
            ? await gateway.Authorize(Money.Zero("USD"), tokenized.Token!, $"verify_{Guid.NewGuid():N}")
            : null;
        if (verified is not { Succeeded: true })
        {
            return Error.Validation("card.rejected", Rejection(tokenized.Decline ?? verified?.Decline)).ToProblem();
        }

        var cards = await Mine(c, ct);
        var card = SavedCard.Create(c.User.UserId!.Value, tokenized.Token!, tokenized.Brand!, tokenized.Last4!, tokenized.ExpMonth, tokenized.ExpYear, c.Clock.GetUtcNow());
        card.IsDefault = cards.Count == 0; // the first card becomes the default
        c.Db.SavedCards.Add(card);
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/me/cards/{card.Id}", ToDto(card, c.Clock.GetUtcNow()));
    }

    private static async Task<IResult> SetDefault(Guid id, [AsParameters] Ctx c, CancellationToken ct)
    {
        var cards = await Mine(c, ct);
        if (cards.All(card => card.Id != id))
        {
            return NotFound();
        }

        // Clear the old default first: the unique "one default" index is checked per statement.
        foreach (var card in cards.Where(card => card.IsDefault && card.Id != id))
        {
            card.IsDefault = false;
        }

        await c.Db.SaveChangesAsync(ct);
        cards.Single(card => card.Id == id).IsDefault = true;
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, [AsParameters] Ctx c, CancellationToken ct)
    {
        var cards = await Mine(c, ct);
        if (cards.SingleOrDefault(card => card.Id == id) is not { } removed)
        {
            return NotFound();
        }

        c.Db.SavedCards.Remove(removed);
        if (removed.IsDefault && cards.LastOrDefault(card => card.Id != id) is { } next)
        {
            await c.Db.SaveChangesAsync(ct);
            next.IsDefault = true; // the newest remaining card takes over
        }

        await c.Db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static string Rejection(DeclineCode? code) => code switch
    {
        DeclineCode.ExpiredCard => "This card has expired.",
        DeclineCode.InvalidCard => "Check the card number, expiry and security code.",
        DeclineCode.InsufficientFunds or DeclineCode.CardDeclined => "Your bank declined this card. Try another card.",
        _ => "This card couldn't be saved. Try another card.",
    };
}
