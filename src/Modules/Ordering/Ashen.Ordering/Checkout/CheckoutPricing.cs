using Ashen.BuildingBlocks;
using Ashen.Catalog.Contracts;
using Ashen.Customers.Contracts;
using Ashen.Ordering.Domain;
using Ashen.Pricing.Contracts;
using Ashen.Promotions.Contracts;

namespace Ashen.Ordering.Checkout;

internal sealed record PricedLine(VariantForCheckout Variant, int Quantity)
{
    public Money LineTotal => (Variant.Price * Quantity).RoundToCents();
}

/// <summary>Everything checkout needs, priced at today's prices. <see cref="Problems"/> block placing the order.</summary>
internal sealed record PricedCheckout(
    IReadOnlyList<PricedLine> Lines,
    AddressSnapshot Address,
    Money Subtotal,
    string? CouponCode,
    Money Discount,
    Money Shipping,
    decimal TaxRate,
    string TaxJurisdiction,
    Money Tax,
    Money Total,
    IReadOnlyList<string> Problems);

/// <summary>Prices a cart for an address: re-checks stock, price and coupon at this moment (FR-019).</summary>
internal sealed class CheckoutPricing(ICatalogModule catalog, ICustomersModule customers, IPricingModule pricing, IPromotionsModule promotions)
{
    public async Task<Result<PricedCheckout>> Price(Cart? cart, Guid customerId, Guid addressId, CancellationToken cancellationToken)
    {
        if (cart is null || cart.Items.Count == 0)
        {
            return Error.Validation("cart.empty", "Your cart is empty.");
        }

        if (await customers.GetAddress(customerId, addressId, cancellationToken) is not { } address)
        {
            return Error.Validation("checkout.address", "Please fix the highlighted fields.",
                new Dictionary<string, string[]> { ["addressId"] = ["Choose one of your delivery addresses."] });
        }

        var variants = await catalog.GetVariantsForCheckout(cart.Items.Select(i => i.VariantId).ToList(), cancellationToken);
        var lines = new List<PricedLine>();
        var problems = new List<string>();
        foreach (var item in cart.Items)
        {
            var variant = variants.SingleOrDefault(v => v.VariantId == item.VariantId);
            if (variant is null || !variant.IsPurchasable)
            {
                problems.Add($"{variant?.ProductName ?? "An item"} is no longer available. Remove it to continue.");
                continue;
            }

            if (item.Quantity > variant.Available)
            {
                problems.Add($"Only {variant.Available} of {variant.ProductName} ({variant.Sku}) left. Lower the quantity to continue.");
            }

            lines.Add(new PricedLine(variant, item.Quantity));
        }

        var currency = (await pricing.GetActiveRegion(cancellationToken)).CurrencyCode;
        var subtotal = lines.Aggregate(Money.Zero(currency), (sum, line) => sum + line.LineTotal);
        var discount = Money.Zero(currency);
        if (cart.CouponCode is { } code)
        {
            var coupon = await promotions.Validate(code, customerId, subtotal, cancellationToken);
            if (coupon.IsValid)
            {
                discount = coupon.Discount;
            }
            else
            {
                problems.Add($"Coupon {coupon.Code}: {coupon.Refusal} Remove it to continue.");
            }
        }

        // Tax is charged on what the customer pays for the goods: after the discount (FR-038).
        var quote = await pricing.Quote(subtotal - discount, address.CountryCode, address.StateCode, cancellationToken);
        var total = (subtotal - discount + quote.Shipping + quote.Tax).RoundToCents();

        return new PricedCheckout(lines, address, subtotal, cart.CouponCode, discount, quote.Shipping, quote.TaxRate, quote.TaxJurisdiction, quote.Tax, total, problems);
    }
}
