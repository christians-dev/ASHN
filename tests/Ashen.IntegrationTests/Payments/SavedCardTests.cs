using System.Net;
using System.Net.Http.Json;

using Ashen.Identity.Contracts;
using Ashen.IntegrationTests.Infrastructure;
using Ashen.IntegrationTests.Ordering;
using Ashen.Payments.Data;
using Ashen.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Ashen.IntegrationTests.Payments;

/// <summary>Saved cards (US8, FR-027, FR-029, SC-009): only a gateway token and display details are ever stored.</summary>
public class SavedCardTests(CheckoutFactory factory) : IClassFixture<CheckoutFactory>
{
    private const string Mastercard = "5555 5555 5555 4444";
    private const string Cvc = "987";

    private sealed record Card(Guid Id, string Brand, string Last4, int ExpMonth, int ExpYear, bool IsDefault, bool IsExpired);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> Save(HttpClient client, string number, int expYear = 2030, string cvc = Cvc) =>
        client.PostAsJsonAsync("/api/me/cards", new { number, expMonth = 12, expYear, cvc }, Ct);

    private static async Task<List<Card>> Cards(HttpClient client) => (await client.GetFromJsonAsync<List<Card>>("/api/me/cards", Ct))!;

    private static Task<HttpResponseMessage> CheckoutWith(HttpClient client, Guid addressId, Guid savedCardId) =>
        client.PostAsJsonAsync("/api/checkout", new { addressId, savedCardId, idempotencyKey = Guid.NewGuid() }, Ct);

    [Fact]
    public async Task Saving_keeps_only_brand_last4_and_expiry()
    {
        var (client, _, _) = await Shopper.New(factory);

        var response = await Save(client, Mastercard);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("5555555555554444", body);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        var card = Assert.Single(await Cards(client));
        Assert.Equal(("Mastercard", "4444", 12, 2030, true, false), (card.Brand, card.Last4, card.ExpMonth, card.ExpYear, card.IsDefault, card.IsExpired));
    }

    [Theory]
    [InlineData("4000 0000 0000 0002", 2030)] // the gateway declines it
    [InlineData("4242 4242 4242 4241", 2030)] // fails the Luhn check
    [InlineData("4242 4242 4242 4242", 2020)] // already expired
    public async Task Declined_or_invalid_cards_are_not_saved(string number, int expYear)
    {
        var (client, _, _) = await Shopper.New(factory);

        var response = await Save(client, number, expYear);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Cards(client));
    }

    [Fact]
    public async Task Default_moves_to_the_chosen_card_and_survives_deleting_the_default()
    {
        var (client, _, _) = await Shopper.New(factory);
        var first = (await (await Save(client, Mastercard)).ReadAs<Card>()).Id;
        var second = (await (await Save(client, "4242 4242 4242 4242")).ReadAs<Card>()).Id;
        Assert.Equal(first, (await Cards(client)).Single(c => c.IsDefault).Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/me/cards/{second}/default", null, Ct)).StatusCode);
        Assert.Equal(second, (await Cards(client)).Single(c => c.IsDefault).Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/cards/{second}", Ct)).StatusCode);
        Assert.Equal(first, Assert.Single(await Cards(client), c => c.IsDefault).Id);
    }

    [Fact]
    public async Task Another_customers_card_is_not_found()
    {
        var (owner, _, _) = await Shopper.New(factory);
        var card = (await (await Save(owner, Mastercard)).ReadAs<Card>()).Id;
        var (stranger, _, addressId) = await Shopper.New(factory);
        var variant = await factory.CreateProduct("Card Thief Tee", 10m, stock: 5);
        await stranger.SetQuantity(variant, 1);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/me/cards/{card}/default", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/me/cards/{card}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CheckoutWith(stranger, addressId, card)).StatusCode);
        Assert.Single(await Cards(owner));
    }

    [Fact]
    public async Task Checkout_with_a_saved_card_places_the_order()
    {
        var (client, _, addressId) = await Shopper.New(factory);
        var card = (await (await Save(client, Mastercard)).ReadAs<Card>()).Id;
        var variant = await factory.CreateProduct("Saved Card Tee", 25m, stock: 5);
        await client.SetQuantity(variant, 1);

        var response = await CheckoutWith(client, addressId, card);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await client.GetFromJsonAsync<OrderCard>($"/api/me/orders/{(await response.ReadAs<Shopper.Placed>()).OrderId}", Ct);
        Assert.Equal(("Mastercard", "4444"), (order!.CardBrand, order.CardLast4));
    }

    private sealed record OrderCard(string CardBrand, string CardLast4);

    [Fact]
    public async Task Expired_saved_card_is_flagged_and_refused_at_checkout()
    {
        var (client, customerId, addressId) = await Shopper.New(factory);
        // A card saved years ago that has since expired.
        var cardId = await factory.Query(async sp =>
        {
            var db = sp.GetRequiredService<PaymentsDbContext>();
            var card = SavedCard.Create(customerId, $"sim_card_Visa_4242_0120_{Guid.NewGuid():N}", "Visa", "4242", 1, 2020, DateTimeOffset.UtcNow);
            db.SavedCards.Add(card);
            await db.SaveChangesAsync();
            return card.Id;
        });
        var variant = await factory.CreateProduct("Expired Card Tee", 10m, stock: 5);
        await client.SetQuantity(variant, 1);

        Assert.True(Assert.Single(await Cards(client)).IsExpired);
        var response = await CheckoutWith(client, addressId, cardId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(5, (await factory.VariantOf(variant)).StockOnHand);
    }

    [Fact]
    public async Task Checkout_needs_exactly_one_payment_method()
    {
        var (client, _, addressId) = await Shopper.New(factory);

        var response = await client.PostAsJsonAsync("/api/checkout", new { addressId, idempotencyKey = Guid.NewGuid() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Account_deletion_removes_saved_cards()
    {
        var (client, customerId, _) = await Shopper.New(factory);
        await Save(client, Mastercard);

        await factory.Query(async sp =>
        {
            await sp.GetRequiredService<Ashen.BuildingBlocks.Events.IEventBus>().Publish(new AccountDeletionRequested(customerId), Ct);
            return 0;
        });

        Assert.Empty(await Cards(client));
    }

    [Fact]
    public async Task No_full_card_number_or_cvc_is_stored_anywhere_in_payments()
    {
        // Exercise every path that sees card details: save, checkout with a new card, checkout with a saved card.
        var (client, _, addressId) = await Shopper.New(factory);
        var saved = (await (await Save(client, Mastercard)).ReadAs<Card>()).Id;
        var variant = await factory.CreateProduct("Scan Tee", 10m, stock: 5);
        await client.SetQuantity(variant, 1);
        await client.Checkout(addressId, new { number = "4242 4242 4242 4242", expMonth = 12, expYear = 2030, cvc = Cvc });
        await client.SetQuantity(variant, 1);
        await CheckoutWith(client, addressId, saved);

        var values = await factory.Query(async sp =>
        {
            var connection = (NpgsqlConnection)sp.GetRequiredService<PaymentsDbContext>().Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = new List<(string Table, string Column)>();
            await using (var find = new NpgsqlCommand(
                "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'payments' AND data_type IN ('text', 'character varying', 'character', 'jsonb')",
                connection))
            await using (var reader = await find.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            var all = new List<string>();
            foreach (var (table, column) in columns)
            {
                await using var read = new NpgsqlCommand($"SELECT \"{column}\"::text FROM payments.\"{table}\" WHERE \"{column}\" IS NOT NULL", connection);
                await using var reader = await read.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    all.Add(reader.GetString(0));
                }
            }

            return all;
        });

        Assert.NotEmpty(values);
        Assert.DoesNotContain(values, v => v.Contains("5555555555554444") || v.Contains("4242424242424242") || v.Contains("5555 5555"));
        Assert.DoesNotContain(values, v => v == Cvc);
    }
}
