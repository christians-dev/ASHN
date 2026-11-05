using Ashen.BuildingBlocks;
using Ashen.Ordering.Domain;

namespace Ashen.Ordering.Shared;

/// <summary>What customers and admins see of an order. Shared because both read the same snapshot.</summary>
internal static class OrderViews
{
    public sealed record OrderSummaryDto(Guid Id, string OrderNumber, DateTimeOffset PlacedAt, OrderStatus Status, Money Total, int ItemCount, string ContactEmail);

    public sealed record OrderLineDto(Guid ProductId, string ProductName, string Sku, Dictionary<string, string> OptionValues, Money UnitPrice, int Quantity, Money LineTotal);

    public sealed record ShipToDto(string RecipientName, string Line1, string? Line2, string City, string StateCode, string PostalCode, string CountryCode, string Phone);

    public sealed record OrderDetailDto(
        Guid Id,
        string OrderNumber,
        DateTimeOffset PlacedAt,
        OrderStatus Status,
        string ContactEmail,
        Money Subtotal,
        string? CouponCode,
        Money Discount,
        Money Shipping,
        decimal TaxRate,
        Money Tax,
        Money Total,
        ShipToDto ShipTo,
        string CardBrand,
        string CardLast4,
        List<OrderLineDto> Lines,
        bool CanCancel);

    public static OrderSummaryDto ToSummary(this Order o) =>
        new(o.Id, o.OrderNumber, o.PlacedAt, o.Status, o.Total, o.Lines.Sum(l => l.Quantity), o.ContactEmail);

    public static OrderDetailDto ToDetail(this Order o, bool canCancel) =>
        new(o.Id, o.OrderNumber, o.PlacedAt, o.Status, o.ContactEmail, o.Subtotal, o.CouponCode, o.Discount, o.Shipping, o.TaxRate, o.Tax, o.Total,
            new ShipToDto(o.ShipTo.RecipientName, o.ShipTo.Line1, o.ShipTo.Line2, o.ShipTo.City, o.ShipTo.StateCode, o.ShipTo.PostalCode, o.ShipTo.CountryCode, o.ShipTo.Phone),
            o.CardBrand,
            o.CardLast4,
            [.. o.Lines.Select(l => new OrderLineDto(l.ProductId, l.ProductName, l.Sku, l.OptionValues.ToDictionary(v => v.Name, v => v.Value), l.UnitPrice, l.Quantity, l.LineTotal))],
            canCancel);
}
