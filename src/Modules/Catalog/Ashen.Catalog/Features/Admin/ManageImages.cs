using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog.Domain;
using Ashen.Catalog.Images;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ashen.Catalog.Features.Admin;

/// <summary>Upload, reorder and delete product images (FR-043).</summary>
internal static class ManageImages
{
    public sealed record ImageOrderRequest(List<Guid> ImageIds);

    public static void Map(RouteGroupBuilder admin)
    {
        // The host's ApiAntiforgery middleware already checks the X-XSRF-TOKEN header on every write.
        admin.MapPost("/products/{id:guid}/images", Upload).DisableAntiforgery().Produces<ManageProducts.AdminImage>(StatusCodes.Status201Created);
        admin.MapPut("/products/{id:guid}/images/order", Reorder);
        admin.MapDelete("/products/{id:guid}/images/{imageId:guid}", Delete);
    }

    private static async Task<IResult> Upload(
        Guid id, IFormFile file, [FromForm] string? altText, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        await using var content = file.OpenReadStream();
        var header = new byte[ImageValidator.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var checkedImage = ImageValidator.Check(header.AsSpan(0, read), file.Length);
        if (!checkedImage.IsSuccess)
        {
            return checkedImage.Error!.ToProblem();
        }

        // Stored under a name we choose: the uploaded file name is never used on disk.
        var path = $"products/{id:N}/{Guid.CreateVersion7():N}{checkedImage.Value}";
        content.Position = 0;
        await c.Storage.Save(path, content, ct);

        var image = product.AddImage(path, string.IsNullOrWhiteSpace(altText) ? product.Name : altText.Trim());
        c.Audit.Record(c.Db, "image.added", nameof(Product), id.ToString(), new { image.Id });
        var saved = await c.Db.TrySaveChangesAsync(ct);
        if (!saved.IsSuccess)
        {
            await c.Storage.Delete(path, ct);
        }

        return saved.ToHttp(() => TypedResults.Created(
            $"/api/admin/products/{id}", new ManageProducts.AdminImage(image.Id, c.Storage.PublicUrl(path), image.AltText, image.SortOrder)));
    }

    private static async Task<IResult> Reorder(Guid id, ImageOrderRequest request, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        var reordered = product.ReorderImages(request.ImageIds);
        if (!reordered.IsSuccess)
        {
            return reordered.Error!.ToProblem();
        }

        c.Audit.Record(c.Db, "image.reordered", nameof(Product), id.ToString());
        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static async Task<IResult> Delete(Guid id, Guid imageId, [AsParameters] ManageProducts.Ctx c, CancellationToken ct)
    {
        if (await ManageProducts.Load(c.Db, id, ct) is not { } product)
        {
            return ManageProducts.NotFound;
        }

        if (product.RemoveImage(imageId) is not { } removed)
        {
            return Error.NotFound("image.not_found", "Image not found.").ToProblem();
        }

        c.Audit.Record(c.Db, "image.removed", nameof(Product), id.ToString(), new { imageId });
        var saved = await c.Db.TrySaveChangesAsync(ct);
        if (saved.IsSuccess)
        {
            await c.Storage.Delete(removed.StoragePath, ct);
        }

        return saved.ToHttp(TypedResults.NoContent);
    }
}
