using Ashen.BuildingBlocks.Data;
using Ashen.BuildingBlocks.Web;
using Ashen.Customers.Data;
using Ashen.Customers.Domain;
using Ashen.Identity.Contracts;
using Ashen.Pricing.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Customers.Features;

/// <summary>The signed-in customer's delivery addresses (FR-016, FR-017). Everything is scoped to their own profile.</summary>
internal static class Addresses
{
    public sealed record AddressRequest(
        string RecipientName, string Line1, string? Line2, string City, string StateCode, string PostalCode, string CountryCode, string Phone);

    public sealed record AddressDto(
        Guid Id, string RecipientName, string Line1, string? Line2, string City, string StateCode, string PostalCode, string CountryCode, string Phone, bool IsDefault);

    internal sealed record Ctx(CustomersDbContext Db, ICurrentUser User, IPricingModule Pricing, TimeProvider Clock);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/me/addresses").WithTags("Addresses");
        me.MapGet("", List).Produces<List<AddressDto>>();
        me.MapPost("", Add).Produces<AddressDto>(StatusCodes.Status201Created);
        me.MapPut("/{id:guid}", Update);
        me.MapDelete("/{id:guid}", Delete);
        me.MapPost("/{id:guid}/default", SetDefault);
    }

    private static async Task<IResult> List([AsParameters] Ctx c, CancellationToken ct)
    {
        var profile = await Load(c, ct);
        return TypedResults.Ok((profile?.Addresses ?? []).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).Select(ToDto).ToList());
    }

    private static async Task<IResult> Add(AddressRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        var details = ToDetails(request);
        if (AddressValidator.Validate(details, await c.Pricing.GetActiveRegion(ct)) is { } invalid)
        {
            return invalid.ToProblem();
        }

        // The profile is created the first time the customer needs one.
        var profile = await Load(c, ct);
        if (profile is null)
        {
            profile = new CustomerProfile(c.User.UserId!.Value, c.Clock.GetUtcNow());
            c.Db.Profiles.Add(profile);
        }

        var address = profile.AddAddress(details);
        await c.Db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/me/addresses/{address.Id}", ToDto(address));
    }

    private static async Task<IResult> Update(Guid id, AddressRequest request, [AsParameters] Ctx c, CancellationToken ct)
    {
        var details = ToDetails(request);
        if (AddressValidator.Validate(details, await c.Pricing.GetActiveRegion(ct)) is { } invalid)
        {
            return invalid.ToProblem();
        }

        return await Change(c, profile => profile.UpdateAddress(id, details), ct);
    }

    private static Task<IResult> Delete(Guid id, [AsParameters] Ctx c, CancellationToken ct) =>
        Change(c, profile => profile.RemoveAddress(id), ct);

    private static Task<IResult> SetDefault(Guid id, [AsParameters] Ctx c, CancellationToken ct) =>
        Change(c, profile => profile.SetDefault(id), ct);

    /// <summary>Another customer's address id is simply "not found": nothing reveals that it exists.</summary>
    private static async Task<IResult> Change(Ctx c, Func<CustomerProfile, BuildingBlocks.Result> change, CancellationToken ct)
    {
        if (await Load(c, ct) is not { } profile)
        {
            return BuildingBlocks.Error.NotFound("address.not_found", "Address not found.").ToProblem();
        }

        var result = change(profile);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        return (await c.Db.TrySaveChangesAsync(ct)).ToHttp(TypedResults.NoContent);
    }

    private static Task<CustomerProfile?> Load(Ctx c, CancellationToken ct) =>
        c.Db.Profiles.Include(p => p.Addresses).SingleOrDefaultAsync(p => p.Id == c.User.UserId, ct);

    private static AddressDetails ToDetails(AddressRequest r) =>
        new(r.RecipientName ?? "", r.Line1 ?? "", r.Line2, r.City ?? "", r.StateCode ?? "", r.PostalCode ?? "", r.CountryCode ?? "", r.Phone ?? "");

    private static AddressDto ToDto(Address a) =>
        new(a.Id, a.RecipientName, a.Line1, a.Line2, a.City, a.StateCode, a.PostalCode, a.CountryCode, a.Phone, a.IsDefault);
}
