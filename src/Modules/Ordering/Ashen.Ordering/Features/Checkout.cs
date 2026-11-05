using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Identity.Contracts;
using Ashen.Ordering.Checkout;
using Ashen.Ordering.Shared;
using Ashen.Payments.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Ordering.Features;

/// <summary>Quote and place an order (FR-019 to FR-021). Signed-in customers only.</summary>
internal static class CheckoutEndpoints
{
    public sealed record QuoteRequest(Guid AddressId);

    public sealed record QuoteResponse(Money Subtotal, string? CouponCode, Money Discount, Money Shipping, decimal TaxRate, Money Tax, Money Total, List<string> Problems);

    public sealed record CardRequest(string Number, int ExpMonth, int ExpYear, string Cvc)
    {
        // Keep the number and CVC out of logs and exception messages.
        public override string ToString() => "CardRequest { ** redacted ** }";
    }

    /// <summary>Pay with a new <see cref="Card"/> or a <see cref="SavedCardId"/>: exactly one of them.</summary>
    public sealed record PlaceOrderRequest(Guid AddressId, CardRequest? Card, Guid? SavedCardId, Guid IdempotencyKey);

    public sealed record PlaceOrderResponse(Guid OrderId, string OrderNumber);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var checkout = endpoints.MapGroup("/checkout").WithTags("Checkout");
        checkout.MapPost("/quote", Quote).Produces<QuoteResponse>();
        checkout.MapPost("", Place).Produces<PlaceOrderResponse>(StatusCodes.Status201Created);
    }

    private static async Task<IResult> Quote(
        QuoteRequest request, CartResolver carts, CheckoutPricing pricing, ICurrentUser user, CancellationToken ct)
    {
        var priced = await pricing.Price(await carts.Find(ct), user.UserId!.Value, request.AddressId, ct);
        return priced.ToHttp(p => TypedResults.Ok(new QuoteResponse(p.Subtotal, p.CouponCode, p.Discount, p.Shipping, p.TaxRate, p.Tax, p.Total, [.. p.Problems])));
    }

    private static async Task<IResult> Place(
        PlaceOrderRequest request, CheckoutOrchestrator orchestrator, IPaymentsModule payments, ICurrentUser user, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            return Error.Validation("checkout.idempotency_key", "A checkout attempt id is required.").ToProblem();
        }

        if ((request.Card is null) == (request.SavedCardId is null))
        {
            return Error.Validation("checkout.payment", "Choose a saved card or enter a new card.").ToProblem();
        }

        PaymentSource source;
        if (request.SavedCardId is { } savedCardId)
        {
            if (await payments.GetCard(user.UserId!.Value, savedCardId, ct) is null)
            {
                return Error.NotFound("card.not_found", "Card not found.").ToProblem();
            }

            source = new SavedCardSource(savedCardId);
        }
        else
        {
            source = new NewCardSource(request.Card!.Number, request.Card.ExpMonth, request.Card.ExpYear, request.Card.Cvc);
        }

        var placed = await orchestrator.Place(request.IdempotencyKey, request.AddressId, source, ct);
        return placed.ToHttp(p => TypedResults.Created($"/api/me/orders/{p.OrderId}", new PlaceOrderResponse(p.OrderId, p.OrderNumber)));
    }
}
