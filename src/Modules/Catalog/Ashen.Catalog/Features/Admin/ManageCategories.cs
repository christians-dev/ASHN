using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Catalog.Features.Admin;

/// <summary>Create, rename, and delete empty categories (FR-042). Listing uses the public /categories.</summary>
internal static class ManageCategories
{
    public sealed record CategoryRequest(string Name);

    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapPost("/categories", Create).Produces<ManageProducts.CreatedResponse>(StatusCodes.Status201Created);
        admin.MapPut("/categories/{id:guid}", Rename);
        admin.MapDelete("/categories/{id:guid}", Delete);
    }

    private static async Task<IResult> Create(CategoryRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var slug = Slug.From(name);
        if (Invalid(name, slug) is { } invalid)
        {
            return invalid.ToProblem();
        }

        if (await c.Db.Categories.AnyAsync(cat => cat.Name.ToLower() == name.ToLower() || cat.Slug == slug, ct))
        {
            return Error.Conflict("category.exists", "A category with this name already exists.").ToProblem();
        }

        var order = await c.Db.Categories.CountAsync(ct);
        var category = new Category(name, slug, order);
        c.Db.Categories.Add(category);
        c.Audit.Record(c.Db, "category.created", nameof(Category), category.Id.ToString(), new { name });
        await c.Db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/categories", new ManageProducts.CreatedResponse(category.Id));
    }

    private static async Task<IResult> Rename(Guid id, CategoryRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await c.Db.Categories.SingleOrDefaultAsync(cat => cat.Id == id, ct) is not { } category)
        {
            return Error.NotFound("category.not_found", "Category not found.").ToProblem();
        }

        var name = request.Name.Trim();
        if (Invalid(name, Slug.From(name)) is { } invalid)
        {
            return invalid.ToProblem();
        }

        if (await c.Db.Categories.AnyAsync(cat => cat.Id != id && cat.Name.ToLower() == name.ToLower(), ct))
        {
            return Error.Conflict("category.exists", "A category with this name already exists.").ToProblem();
        }

        category.Rename(name);
        c.Audit.Record(c.Db, "category.renamed", nameof(Category), id.ToString(), new { name });
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await c.Db.Categories.SingleOrDefaultAsync(cat => cat.Id == id, ct) is not { } category)
        {
            return Error.NotFound("category.not_found", "Category not found.").ToProblem();
        }

        if (await c.Db.Products.AnyAsync(p => p.CategoryId == id, ct))
        {
            return Error.Conflict("category.not_empty", "Move or delete this category's products first.").ToProblem();
        }

        c.Db.Categories.Remove(category);
        c.Audit.Record(c.Db, "category.deleted", nameof(Category), id.ToString(), new { category.Name });
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Error? Invalid(string name, string slug) =>
        name.Length is 0 or > 60 || slug.Length == 0
            ? Error.Validation("category.invalid", "Please fix the highlighted fields.",
                new Dictionary<string, string[]> { ["name"] = ["Enter a name of up to 60 characters."] })
            : null;
}
