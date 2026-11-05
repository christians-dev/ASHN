using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

using Ashen.IntegrationTests.Infrastructure;

namespace Ashen.IntegrationTests.Catalog;

/// <summary>Seeds the optional 1,000-product catalog for the performance check.</summary>
public sealed class LargeCatalogFactory : AshenApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Catalog:Seed:LargeCatalog"] = "1000",
    };
}

/// <summary>
/// SC-005: with 1,000 products, product lists and product pages respond within 2 seconds for 95% of views.
/// This measures the server's share (queries and serialisation) in-process; the k6 script in tests/load measures
/// it end to end under 200 concurrent shoppers (SC-006).
/// </summary>
public class CatalogPerformanceTests(LargeCatalogFactory factory) : IClassFixture<LargeCatalogFactory>
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private sealed record Item(string Slug);
    private sealed record Page(List<Item> Items, int TotalCount);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The 95th-percentile duration of the requests.</summary>
    private static async Task<TimeSpan> P95(HttpClient client, IReadOnlyList<string> urls)
    {
        await client.GetAsync(urls[0], Ct); // warm up JIT and connection pool
        var durations = new List<TimeSpan>();
        foreach (var url in urls)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync(url, Ct);
            durations.Add(watch.Elapsed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var p95 = durations.Order().ElementAt((int)Math.Ceiling(durations.Count * 0.95) - 1);
        TestContext.Current.TestOutputHelper?.WriteLine($"p95 {p95.TotalMilliseconds:0} ms, max {durations.Max().TotalMilliseconds:0} ms over {durations.Count} requests");
        return p95;
    }

    [Fact]
    public async Task Product_lists_stay_under_2_seconds_at_p95()
    {
        var client = factory.CreateApiClient();
        var total = (await client.GetFromJsonAsync<Page>("/api/products?pageSize=1", Ct))!.TotalCount;
        Assert.True(total >= 1000, $"expected the 1,000-product seed, found {total}");

        string[] shapes =
        [
            "/api/products?page={0}",
            "/api/products?category=load-test&page={0}",
            "/api/products?sort=price_asc&page={0}",
            "/api/products?sort=rating&page={0}",
            "/api/products?q=item&size=M&colour=black&page={0}",
        ];
        var urls = Enumerable.Range(0, 100).Select(i => string.Format(shapes[i % shapes.Length], i % 40 + 1)).ToList();

        var p95 = await P95(client, urls);

        Assert.True(p95 < Budget, $"p95 was {p95.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task Product_pages_stay_under_2_seconds_at_p95()
    {
        var client = factory.CreateApiClient();
        var slugs = new List<string>();
        for (var page = 1; slugs.Count < 100; page++)
        {
            slugs.AddRange((await client.GetFromJsonAsync<Page>($"/api/products?category=load-test&pageSize=100&page={page}", Ct))!.Items.Select(i => i.Slug));
        }

        var p95 = await P95(client, [.. slugs.Take(100).Select(s => $"/api/products/{s}")]);

        Assert.True(p95 < Budget, $"p95 was {p95.TotalMilliseconds:0} ms");
    }
}
