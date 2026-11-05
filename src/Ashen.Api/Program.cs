using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Ashen.Api;
using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Web;
using Ashen.Catalog;
using Ashen.Customers;
using Ashen.Identity;
using Ashen.Identity.Contracts;
using Ashen.Ordering;
using Ashen.Payments;
using Ashen.Pricing;
using Ashen.Promotions;
using Ashen.Reviews;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The store is a set of modules; each registers its own services and endpoints (constitution I).
IModule[] modules =
[
    new IdentityModule(),
    new PricingModule(),
    new CatalogModule(),
    new CustomersModule(),
    new PaymentsModule(),
    new OrderingModule(),
    new PromotionsModule(),
    new ReviewsModule(),
];

builder.Services.AddBuildingBlocks();
foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

builder.Services.AddProblemDetails();
// Numbers are numbers: no "12.50" strings in requests, and clean number types in the OpenAPI document.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // "Published", not 1
});
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(options => options.HeaderName = ApiAntiforgery.HeaderName);

// Every endpoint needs a signed-in user unless it opts out with AllowAnonymous (constitution VI).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(Roles.Admin));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Ashen:RateLimits:AuthPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

// No bodies, headers, cookies or query strings in request logs (constitution IX).
builder.Services.AddHttpLogging(options => options.LoggingFields =
    HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration);

builder.Services.AddScoped<DatabaseInitializer>();

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseHttpLogging();
app.UseStaticFiles();
// Product images; the Catalog module writes them under the same root (Catalog:Media:RootPath).
var mediaRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, app.Configuration["Catalog:Media:RootPath"] ?? "media"));
Directory.CreateDirectory(mediaRoot);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(mediaRoot), RequestPath = "/media" });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapDefaultEndpoints();

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapEndpoints(api);
}

// Unknown API routes are a 404 problem, never the SPA page.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound)).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().Run(app.Lifetime.ApplicationStopping);
}

app.Run();

public partial class Program;
