using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Audit;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Data;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;
using Ashen.Catalog.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features.Admin;

/// <summary>Admins create, edit, publish, unpublish, archive and delete products (FR-042, FR-044).</summary>
internal static class ManageProducts
{
    public sealed record AdminProductRow(Guid Id, string Name, string Slug, ProductStatus Status, string CategoryName, int VariantCount, bool IsFeatured);

    public sealed record AdminProductDetail(
        Guid Id,
        string Name,
        string Slug,
        string Description,
        Guid CategoryId,
        ProductStatus Status,
        bool IsFeatured,
        bool HasBeenOrdered,
        List<GetProduct.OptionDto> Options,
        List<AdminVariant> Variants,
        List<AdminImage> Images);

    public sealed record AdminVariant(Guid Id, string Sku, Dictionary<string, string> OptionValues, Money Price, int StockOnHand, int StockReserved);
    public sealed record AdminImage(Guid Id, string Url, string AltText, int SortOrder);
    public sealed record ProductDetailsRequest(string Name, string Description, Guid CategoryId);
    public sealed record CreatedResponse(Guid Id);

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/products", List).Produces<List<AdminProductRow>>();
        admin.MapGet("/products/{id:guid}", Get).Produces<AdminProductDetail>();
        admin.MapPost("/products", Create).Produces<CreatedResponse>(StatusCodes.Status201Created);
        admin.MapPut("/products/{id:guid}", Update);
        admin.MapPost("/products/{id:guid}/publish", (Guid id, [AsParameters] Ctx c, CancellationToken ct) => ChangeStatus(id, c, "product.published", (p, now) => p.Publish(now), ct));
        admin.MapPost("/products/{id:guid}/unpublish", (Guid id, [AsParameters] Ctx c, CancellationToken ct) => ChangeStatus(id, c, "product.unpublished", (p, _) => p.Unpublish(), ct));
        admin.MapPost("/products/{id:guid}/archive", (Guid id, [AsParameters] Ctx c, CancellationToken ct) => ChangeStatus(id, c, "product.archived", (p, _) => { p.Archive(); return Result.Success(); }, ct));
        admin.MapDelete("/products/{id:guid}", Delete);
    }

    /// <summary>The services every admin product slice needs, bound in one go.</summary>
    internal sealed record Ctx(CatalogDbContext Db, IAuditLog Audit, IFileStorage Storage, TimeProvider Clock);

    internal static Task<Product?> Load(CatalogDbContext db, Guid id, CancellationToken ct) =>
        db.Products.Include(p => p.Variants).Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == id, ct);

    internal static readonly IResult NotFound = Error.NotFound("product.not_found", "Product not found.").ToProblem();

    private static async Task<IResult> List(ProductStatus? status, CatalogDbContext db, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();
        if (status is not null)
        {
            products = products.Where(p => p.Status == status);
        }

        return TypedResults.Ok(await products
            .OrderBy(p => p.Name)
            .Select(p => new AdminProductRow(
                p.Id, p.Name, p.Slug, p.Status,
                db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).Single(),
                p.Variants.Count, p.IsFeatured))
            .ToListAsync(ct));
    }

    private static async Task<IResult> Get(Guid id, [AsParameters] Ctx c, CancellationToken ct) =>
        await Load(c.Db, id, ct) is { } product ? TypedResults.Ok(ToDetail(product, c.Storage)) : NotFound;

    private static async Task<IResult> Create(ProductDetailsRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await Validate(request, c.Db, ct) is { } invalid)
        {
            return invalid.ToProblem();
        }

        var slug = Slug.From(request.Name);
        if (await c.Db.Products.AnyAsync(p => p.Slug == slug, ct))
        {
            return Error.Conflict("product.slug_taken", "A product with this name already exists.").ToProblem();
        }

        var product = Product.Create(request.Name.Trim(), slug, request.Description.Trim(), request.CategoryId, c.Clock.GetUtcNow());
        c.Db.Products.Add(product);
        c.Audit.Record(c.Db, "product.created", nameof(Product), product.Id.ToString(), new { product.Name });
        await c.Db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/products/{product.Id}", new CreatedResponse(product.Id));
    }

    private static async Task<IResult> Update(Guid id, ProductDetailsRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await Load(c.Db, id, ct) is not { } product)
        {
            return NotFound;
        }

        if (await Validate(request, c.Db, ct) is { } invalid)
        {
            return invalid.ToProblem();
        }

        product.UpdateDetails(request.Name.Trim(), request.Description.Trim(), request.CategoryId);
        c.Audit.Record(c.Db, "product.updated", nameof(Product), id.ToString(), new { product.Name });
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static async Task<IResult> ChangeStatus(
        Guid id, Ctx c, string action, Func<Product, DateTimeOffset, Result> change, CancellationToken ct)
    {
        if (await Load(c.Db, id, ct) is not { } product)
        {
            return NotFound;
        }

        var changed = change(product, c.Clock.GetUtcNow());
        if (!changed.IsSuccess)
        {
            return changed.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, action, nameof(Product), id.ToString());
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    /// <summary>Products that were ever ordered are archived instead, so order history stays intact (FR-044).</summary>
    private static async Task<IResult> Delete(Guid id, [AsParameters] Ctx c, CancellationToken ct)
    {
        if (await Load(c.Db, id, ct) is not { } product)
        {
            return NotFound;
        }

        if (product.HasBeenOrdered)
        {
            product.Archive();
            c.Audit.Record(c.Db, "product.archived", nameof(Product), id.ToString(), new { Reason = "delete requested on an ordered product" });
            return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
        }

        var files = product.Images.Select(i => i.StoragePath).ToList();
        c.Db.Products.Remove(product);
        c.Audit.Record(c.Db, "product.deleted", nameof(Product), id.ToString(), new { product.Name });
        var saved = await c.Db.TrySaveChangesAsync(ct);
        if (saved.IsSuccess)
        {
            // Only after the row is gone: a failed save must not leave a product pointing at missing files.
            foreach (var file in files)
            {
                await c.Storage.Delete(file, ct);
            }
        }

        return saved.ToHttp(TypedResults.NoContent);
    }

    internal static AdminProductDetail ToDetail(Product p, IFileStorage storage) => new(
        p.Id, p.Name, p.Slug, p.Description, p.CategoryId, p.Status, p.IsFeatured, p.HasBeenOrdered,
        p.Options.Select(o => new GetProduct.OptionDto(o.Name, o.Values)).ToList(),
        p.Variants.OrderBy(v => v.Sku).Select(v => new AdminVariant(
            v.Id, v.Sku, v.OptionValues.ToDictionary(o => o.Name, o => o.Value), v.Price, v.StockOnHand, v.StockReserved)).ToList(),
        p.Images.OrderBy(i => i.SortOrder).Select(i => new AdminImage(i.Id, storage.PublicUrl(i.StoragePath), i.AltText, i.SortOrder)).ToList());

    private static async Task<Error?> Validate(ProductDetailsRequest request, CatalogDbContext db, CancellationToken ct)
    {
        var fields = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120 || Slug.From(request.Name).Length == 0)
        {
            fields["name"] = ["Enter a name of up to 120 characters, including at least one letter or digit."];
        }

        if (request.Description.Length > 4000)
        {
            fields["description"] = ["Keep the description under 4,000 characters."];
        }

        if (!await db.Categories.AnyAsync(cat => cat.Id == request.CategoryId, ct))
        {
            fields["categoryId"] = ["Choose a category."];
        }

        return fields.Count == 0 ? null : Error.Validation("product.invalid", "Please fix the highlighted fields.", fields);
    }
}
