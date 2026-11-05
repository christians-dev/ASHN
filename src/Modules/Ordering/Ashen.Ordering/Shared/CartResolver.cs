using System.Security.Cryptography;

using Ashen.Identity.Contracts;
using Ashen.Ordering.Data;
using Ashen.Ordering.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ashen.Ordering.Shared;

/// <summary>
/// Finds the shopper's cart: the customer's own, or a guest's via the <c>ashen_cart</c> cookie. When a signed-in
/// request still carries a guest cookie, the guest cart is merged in and the cookie deleted (FR-018). Every cart
/// and checkout slice goes through here, so Identity never needs to know about carts.
/// </summary>
internal sealed class CartResolver(OrderingDbContext db, IHttpContextAccessor httpContextAccessor, ICurrentUser user, TimeProvider clock)
{
    public const string CookieName = "ashen_cart";

    private HttpContext Http => httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HTTP request.");

    /// <summary>The current cart, or null if the shopper hasn't got one yet.</summary>
    public Task<Cart?> Find(CancellationToken cancellationToken) => Resolve(create: false, cancellationToken);

    public async Task<Cart> GetOrCreate(CancellationToken cancellationToken) => (await Resolve(create: true, cancellationToken))!;

    private async Task<Cart?> Resolve(bool create, CancellationToken cancellationToken)
    {
        var token = Http.Request.Cookies[CookieName];
        var guest = token is null ? null : await Carts().SingleOrDefaultAsync(c => c.AnonymousToken == token, cancellationToken);
        var now = clock.GetUtcNow();

        if (user.UserId is { } customerId)
        {
            var mine = await Carts().SingleOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
            if (guest is not null)
            {
                if (mine is null)
                {
                    mine = Cart.ForCustomer(customerId, now);
                    db.Carts.Add(mine);
                }

                mine.MergeFrom(guest, now);
                db.Carts.Remove(guest);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (token is not null)
            {
                Http.Response.Cookies.Delete(CookieName, CookieOptions());
            }

            if (mine is null && create)
            {
                mine = Cart.ForCustomer(customerId, now);
                db.Carts.Add(mine);
            }

            return mine;
        }

        if (guest is null && create)
        {
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            guest = Cart.ForGuest(token, now);
            db.Carts.Add(guest);
            Http.Response.Cookies.Append(CookieName, token, CookieOptions());
        }

        return guest;
    }

    private IQueryable<Cart> Carts() => db.Carts.Include(c => c.Items);

    // Unguessable token, never readable by scripts.
    private static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromDays(30),
    };
}
