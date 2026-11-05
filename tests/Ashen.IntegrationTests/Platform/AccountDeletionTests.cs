using System.Net;
using System.Net.Http.Json;

using Ashen.Catalog.Data;
using Ashen.Customers.Data;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;
using Ashen.Ordering.Data;
using Ashen.Payments.Data;
using Ashen.Reviews.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Platform;

/// <summary>
/// Deleting an account reaches every module through one event (FR-015): personal data goes, the store's records
/// (orders, reviews) stay without it, and the old credentials no longer work.
/// </summary>
public class AccountDeletionTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private const string Password = "correct horse battery";

    private sealed record Session(Guid? UserId);
    private sealed record Created(Guid Id);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_an_account_removes_personal_data_everywhere_and_keeps_the_records()
    {
        // A real cookie account that has used every module.
        var email = $"leaving-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateApiClient();
        await client.RefreshXsrfToken();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName = "Jordan Leaving" }, Ct)).StatusCode);
        await client.RefreshXsrfToken();
        var userId = (await client.GetFromJsonAsync<Session>("/api/auth/session", Ct))!.UserId!.Value;

        var addressId = await client.AddAddress("IL", "60601");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/me/cards", new { number = "5555 5555 5555 4444", expMonth = 12, expYear = 2030, cvc = "123" }, Ct)).StatusCode);
        var variant = await factory.CreateProduct("Farewell Tee", 40m, stock: 5);
        var productId = await factory.Query(sp => sp.GetRequiredService<CatalogDbContext>().Products
            .Where(p => p.Variants.Any(v => v.Id == variant)).Select(p => p.Id).SingleAsync());
        await client.SetQuantity(variant, 1);
        var orderId = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;
        var admin = await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");
        await admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct);
        await admin.PostAsync($"/api/admin/orders/{orderId}/deliver", null, Ct);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/products/{productId}/reviews/mine", new { rating = 5, text = "Lovely" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/me/wishlist/{productId}", null, Ct)).StatusCode);
        var totalBefore = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.Where(o => o.Id == orderId).Select(o => o.Total.Amount).SingleAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account", Ct)).StatusCode);

        // Customers: profile, addresses and wishlist gone.
        Assert.False(await factory.Query(sp => sp.GetRequiredService<CustomersDbContext>().Profiles.AnyAsync(p => p.Id == userId)));
        // Payments: saved cards gone.
        Assert.False(await factory.Query(sp => sp.GetRequiredService<PaymentsDbContext>().SavedCards.AnyAsync(c => c.CustomerId == userId)));
        // Reviews: kept, credited to a former customer.
        var review = await factory.Query(sp => sp.GetRequiredService<ReviewsDbContext>().Reviews.AsNoTracking().SingleAsync(r => r.CustomerId == userId));
        Assert.Equal(("Former customer", 5), (review.AuthorDisplayName, review.Rating));
        // Ordering: personal details stripped, amounts kept.
        var order = await factory.Query(sp => sp.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.Equal(("", "Deleted customer", "", totalBefore), (order.ContactEmail, order.ShipTo.RecipientName, order.ShipTo.Phone, order.Total.Amount));
        Assert.DoesNotContain("Main St", order.ShipTo.Line1);

        // Identity: the old email and password no longer sign in.
        var again = factory.CreateApiClient();
        await again.RefreshXsrfToken();
        Assert.NotEqual(HttpStatusCode.NoContent, (await again.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct)).StatusCode);
    }
}
