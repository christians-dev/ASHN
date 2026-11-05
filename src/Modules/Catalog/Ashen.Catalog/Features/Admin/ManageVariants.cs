using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Domain;
using Ashen.Pricing.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features.Admin;

/// <summary>Option definitions, variants and stock (FR-003, FR-045). The rules live in <see cref="Product"/>.</summary>
internal static class ManageVariants
{
    public sealed record OptionsRequest(List<GetProduct.OptionDto> Options);
    public sealed record VariantRequest(string Sku, Dictionary<string, string> OptionValues, decimal Price, int StockOnHand);
    public sealed record UpdateVariantRequest(Dictionary<string, string> OptionValues, decimal Price);
    public sealed record StockRequest(int StockOnHand);

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapPut("/products/{id:guid}/options", SetOptions);
        admin.MapPost("/products/{id:guid}/variants", Add).Produces<ManageProducts.CreatedResponse>(StatusCodes.Status201Created);
        admin.MapPut("/products/{id:guid}/variants/{variantId:guid}", Update);
        admin.MapPut("/products/{id:guid}/variants/{variantId:guid}/stock", SetStock);
    }

    private static async Task<IResult> SetOptions(Guid id, OptionsRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        var defined = product.DefineOptions(request.Options.Select(o => new ProductOption(o.Name, o.Values)).ToList());
        if (!defined.IsSuccess)
        {
            return defined.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, "product.options_set", nameof(Product), id.ToString(), request.Options);
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static async Task<IResult> Add(
        Guid id, VariantRequest request, [AsParameters] ManageProducts.Ctx c, IPricingModule pricing, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        var sku = request.Sku.Trim();
        if (sku.Length is 0 or > 60)
        {
            return Error.Validation("variant.sku", "Please fix the highlighted fields.",
                new Dictionary<string, string[]> { ["sku"] = ["Enter a SKU of up to 60 characters."] }).ToProblem();
        }

        if (await c.Db.Variants.AnyAsync(v => v.Sku == sku, ct))
        {
            return Error.Conflict("variant.sku_taken", $"SKU \"{sku}\" is already used.").ToProblem();
        }

        var price = await Price(request.Price, pricing, ct);
        var added = product.AddVariant(sku, request.OptionValues, price, request.StockOnHand);
        if (!added.IsSuccess)
        {
            return added.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, "variant.added", nameof(Product), id.ToString(), new { sku, request.Price, request.StockOnHand });
        var saved = await c.Db.TrySaveChangesAsync(ct);
        return saved.ToHttp(() => TypedResults.Created($"/api/admin/products/{id}", new ManageProducts.CreatedResponse(added.Value.Id)));
    }

    private static async Task<IResult> Update(
        Guid id, Guid variantId, UpdateVariantRequest request, [AsParameters] ManageProducts.Ctx c, IPricingModule pricing, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        var updated = product.UpdateVariant(variantId, request.OptionValues, await Price(request.Price, pricing, ct));
        if (!updated.IsSuccess)
        {
            return updated.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, "variant.updated", nameof(Product), id.ToString(), new { variantId, request.Price });
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static async Task<IResult> SetStock(Guid id, Guid variantId, StockRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        var set = product.SetStock(variantId, request.StockOnHand);
        if (!set.IsSuccess)
        {
            return set.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, "variant.stock_set", nameof(Product), id.ToString(), new { variantId, request.StockOnHand });
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    // Prices are in the store's region currency, never a hard-coded one (constitution III).
    private static async Task<Money> Price(decimal amount, IPricingModule pricing, CancellationToken ct) =>
        new Money(amount, (await pricing.GetActiveRegion(ct)).CurrencyCode).RoundToCents();
}
