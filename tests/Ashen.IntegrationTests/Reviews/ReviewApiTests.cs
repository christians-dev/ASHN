using System.Net;
using System.Net.Http.Json;

using Ashen.BuildingBlocks.Audit;
using Ashen.Identity.Contracts;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;
using Ashen.Reviews.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashen.IntegrationTests.Reviews;

/// <summary>Reviews from verified buyers, ratings on product pages and lists, and moderation (FR-039, FR-040).</summary>
public class ReviewApiTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private sealed record Summary(decimal Average, int Count);
    private sealed record ReviewDto(Guid Id, string Author, int Rating, string? Text);
    private sealed record ProductReviews(Summary? Summary, List<ReviewDto> Reviews, ReviewDto? Mine, bool MineIsHidden, bool CanReview);
    private sealed record ListItem(Guid ProductId, string Name, decimal? RatingAverage, int RatingCount);
    private sealed record Page(List<ListItem> Items);
    private sealed record Detail(Guid Id, Summary? Rating);
    private sealed record AdminReview(Guid Id, string ProductName, bool IsHidden);
    private sealed record AdminPage(List<AdminReview> Items);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> Admin() => await factory.CreateApiClient().ActAs(Guid.CreateVersion7(), "Admin");

    private async Task<(Guid ProductId, string Slug)> NewProduct(string name) => await ProductOf(await factory.CreateProduct(name, 30m, stock: 20));

    private async Task<(Guid ProductId, string Slug)> ProductOf(Guid variantId)
    {
        var product = await factory.Query(sp => sp.GetRequiredService<Ashen.Catalog.Data.CatalogDbContext>().Products
            .Where(p => p.Variants.Any(v => v.Id == variantId)).Select(p => new { p.Id, p.Slug }).SingleAsync());
        return (product.Id, product.Slug);
    }

    /// <summary>A customer whose order for the product has been delivered.</summary>
    private async Task<(HttpClient Client, Guid CustomerId)> VerifiedBuyer(Guid productId)
    {
        var variant = await factory.Query(sp => sp.GetRequiredService<Ashen.Catalog.Data.CatalogDbContext>().Products
            .Where(p => p.Id == productId).SelectMany(p => p.Variants).Select(v => v.Id).FirstAsync());
        var (client, customerId, addressId) = await Shopper.New(factory);
        await client.SetQuantity(variant, 1);
        var orderId = (await (await client.Checkout(addressId, Shopper.GoodCard)).ReadAs<Shopper.Placed>()).OrderId;
        var admin = await Admin();
        await admin.PostAsync($"/api/admin/orders/{orderId}/ship", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/orders/{orderId}/deliver", null, Ct)).StatusCode);
        return (client, customerId);
    }

    private static Task<HttpResponseMessage> Review(HttpClient client, Guid productId, int rating, string? text = null) =>
        client.PutAsJsonAsync($"/api/products/{productId}/reviews/mine", new { rating, text }, Ct);

    private async Task<ProductReviews> ReviewsOf(Guid productId, HttpClient? client = null) =>
        (await (client ?? factory.CreateApiClient()).GetFromJsonAsync<ProductReviews>($"/api/products/{productId}/reviews", Ct))!;

    [Fact]
    public async Task Verified_buyer_reviews_and_a_second_submit_updates_it()
    {
        var (productId, _) = await NewProduct("Reviewed Tee");
        var (client, _) = await VerifiedBuyer(productId);
        Assert.True((await ReviewsOf(productId, client)).CanReview);

        Assert.Equal(HttpStatusCode.OK, (await Review(client, productId, 4, "Soft and warm")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Review(client, productId, 5, "Even better after washing")).StatusCode);

        var reviews = await ReviewsOf(productId);
        var review = Assert.Single(reviews.Reviews);
        Assert.Equal((5, "Even better after washing", "Customer"), (review.Rating, review.Text, review.Author));
        Assert.Equal(new Summary(5.0m, 1), reviews.Summary);
    }

    [Fact]
    public async Task Customers_without_a_delivered_order_cannot_review()
    {
        var (productId, _) = await NewProduct("Unbought Tee");
        var (stranger, _, _) = await Shopper.New(factory);

        // Paid but not yet delivered doesn't count either.
        var variant = await factory.CreateProduct("Undelivered Tee", 10m, stock: 5);
        var (undeliveredProduct, _) = await ProductOf(variant);
        var (buyer, _, addressId) = await Shopper.New(factory);
        await buyer.SetQuantity(variant, 1);
        await buyer.Checkout(addressId, Shopper.GoodCard);

        Assert.False((await ReviewsOf(productId, stranger)).CanReview);
        Assert.Equal(HttpStatusCode.Forbidden, (await Review(stranger, productId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Review(buyer, undeliveredProduct, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Review(factory.CreateApiClient(), productId, 5)).StatusCode);
    }

    [Fact]
    public async Task Invalid_reviews_are_rejected()
    {
        var (productId, _) = await NewProduct("Picky Tee");
        var (client, _) = await VerifiedBuyer(productId);

        Assert.Equal(HttpStatusCode.BadRequest, (await Review(client, productId, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Review(client, productId, 3, new string('x', 2001))).StatusCode);
    }

    [Fact]
    public async Task Hidden_reviews_leave_the_list_and_the_average_but_the_author_still_sees_theirs()
    {
        var (productId, slug) = await NewProduct("Moderated Tee");
        var (happy, _) = await VerifiedBuyer(productId);
        var (grumpy, _) = await VerifiedBuyer(productId);
        await Review(happy, productId, 5);
        var hiddenId = (await (await Review(grumpy, productId, 1, "spam spam")).ReadAs<ReviewDto>()).Id;
        Assert.Equal(new Summary(3.0m, 2), (await ReviewsOf(productId)).Summary);

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/api/admin/reviews/{hiddenId}/hide", null, Ct)).StatusCode);

        var publicView = await ReviewsOf(productId);
        Assert.Equal(new Summary(5.0m, 1), publicView.Summary);
        Assert.DoesNotContain(publicView.Reviews, r => r.Id == hiddenId);
        Assert.Equal(new Summary(5.0m, 1), (await factory.CreateApiClient().GetFromJsonAsync<Detail>($"/api/products/{slug}", Ct))!.Rating);
        var authorView = await ReviewsOf(productId, grumpy);
        Assert.Equal((hiddenId, true), (authorView.Mine!.Id, authorView.MineIsHidden));

        await (await Admin()).PostAsync($"/api/admin/reviews/{hiddenId}/unhide", null, Ct);
        Assert.Equal(new Summary(3.0m, 2), (await ReviewsOf(productId)).Summary);
    }

    [Fact]
    public async Task Product_list_shows_ratings_and_sorts_by_rating()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (good, _) = await NewProduct($"Rated Good {tag}");
        var (okay, _) = await NewProduct($"Rated Okay {tag}");
        await NewProduct($"Rated None {tag}");
        await Review((await VerifiedBuyer(good)).Client, good, 5);
        await Review((await VerifiedBuyer(okay)).Client, okay, 3);

        var page = (await factory.CreateApiClient().GetFromJsonAsync<Page>($"/api/products?q={tag}&sort=rating", Ct))!;

        Assert.Equal([$"Rated Good {tag}", $"Rated Okay {tag}", $"Rated None {tag}"], page.Items.Select(i => i.Name));
        Assert.Equal((5.0m, 1), (page.Items[0].RatingAverage!.Value, page.Items[0].RatingCount));
        Assert.Equal((null, 0), (page.Items[2].RatingAverage, page.Items[2].RatingCount));
    }

    [Fact]
    public async Task Customer_deletes_their_review()
    {
        var (productId, _) = await NewProduct("Regret Tee");
        var (client, _) = await VerifiedBuyer(productId);
        await Review(client, productId, 2);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{productId}/reviews/mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/products/{productId}/reviews/mine", Ct)).StatusCode);
        Assert.Null((await ReviewsOf(productId)).Summary);
    }

    [Fact]
    public async Task Account_deletion_credits_reviews_to_a_former_customer()
    {
        var (productId, _) = await NewProduct("Orphan Tee");
        var (client, customerId) = await VerifiedBuyer(productId);
        await Review(client, productId, 4);

        await factory.Query(async sp =>
        {
            await sp.GetRequiredService<Ashen.BuildingBlocks.Events.IEventBus>().Publish(new AccountDeletionRequested(customerId), Ct);
            return 0;
        });

        Assert.Equal("Former customer", Assert.Single((await ReviewsOf(productId)).Reviews).Author);
    }

    [Fact]
    public async Task Admin_lists_reviews_by_visibility_and_actions_are_audited()
    {
        var (productId, _) = await NewProduct("Listed Review Tee");
        var (client, _) = await VerifiedBuyer(productId);
        var id = (await (await Review(client, productId, 2)).ReadAs<ReviewDto>()).Id;
        var admin = await Admin();
        await admin.PostAsync($"/api/admin/reviews/{id}/hide", null, Ct);

        var hidden = (await admin.GetFromJsonAsync<AdminPage>("/api/admin/reviews?hidden=true&pageSize=100", Ct))!;
        var visible = (await admin.GetFromJsonAsync<AdminPage>("/api/admin/reviews?hidden=false&pageSize=100", Ct))!;

        Assert.Equal("Listed Review Tee", hidden.Items.Single(r => r.Id == id).ProductName);
        Assert.DoesNotContain(visible.Items, r => r.Id == id);
        var actions = await factory.Query(sp => sp.GetRequiredService<ReviewsDbContext>().Set<AuditEntry>()
            .Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Equal(["review.hidden"], actions);
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/reviews" },
        { "POST", $"/api/admin/reviews/{Guid.Empty}/hide" },
        { "POST", $"/api/admin/reviews/{Guid.Empty}/unhide" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Customers_are_forbidden_from_moderation_routes(string method, string path)
    {
        var (client, _, _) = await Shopper.New(factory);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
