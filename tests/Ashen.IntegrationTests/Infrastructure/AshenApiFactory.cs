using Ashen.BuildingBlocks.Email;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Ashen.IntegrationTests.Infrastructure;

/// <summary>
/// The real API against a real PostgreSQL (constitution VII). One container is shared by the whole test run;
/// each test class gets its own fresh database, migrated on start-up and dropped afterwards.
/// </summary>
public class AshenApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string InitialAdminEmail = "admin@test.local";

    private static readonly Lazy<Task<PostgreSqlContainer>> SharedContainer = new(StartContainer);

    private readonly string _databaseName = $"ashen_test_{Guid.NewGuid():N}";
    private string _serverConnectionString = "";

    public InMemoryEmailSender Emails { get; } = new();

    /// <summary>Extra settings a test class needs before the host starts.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    public virtual async ValueTask InitializeAsync()
    {
        var container = await SharedContainer.Value;
        _serverConnectionString = container.GetConnectionString();

        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {_databaseName}", connection);
        await create.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = _databaseName,
            // Test classes share one PostgreSQL (100 connections max): queue for a connection rather than fail.
            MaxPoolSize = 20,
        }.ToString();

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ashen", connectionString);
        builder.UseSetting("Ashen:MigrateOnStartup", "true");
        builder.UseSetting("Ashen:Store:BaseUrl", "https://localhost");
        builder.UseSetting("Ashen:InitialAdmin:Email", InitialAdminEmail);
        builder.UseSetting("Email:Smtp:Host", "unused.invalid");
        // Every test request comes from the same "IP"; the real limit would trip within a test class.
        builder.UseSetting("Ashen:RateLimits:AuthPerMinute", "10000");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);

            // Tests can act as a user by sending X-Test-User / X-Test-Roles headers; otherwise the normal cookie applies.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.SelectorScheme, null, options => options.ForwardDefaultSelector = http =>
                    http.Request.Headers.ContainsKey(TestAuthHandler.UserHeader)
                        ? TestAuthHandler.SchemeName
                        : IdentityConstants.ApplicationScheme);
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SelectorScheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.SelectorScheme;
                options.DefaultChallengeScheme = TestAuthHandler.SelectorScheme;
            });
        });
    }

    /// <summary>An HTTPS client (so Secure cookies flow) that keeps cookies between requests.</summary>
    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    /// <summary>Runs a query against the app's services in a fresh scope, e.g. to read a module's DbContext.</summary>
    public async Task<T> Query<T>(Func<IServiceProvider, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
        GC.SuppressFinalize(this);
    }

    private static async Task<PostgreSqlContainer> StartContainer()
    {
        // Requires Docker. Ryuk (Testcontainers' reaper) removes the container when the test run ends.
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();
        return container;
    }
}
