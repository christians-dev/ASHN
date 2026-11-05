using Microsoft.AspNetCore.Antiforgery;

namespace Ashen.Api;

internal static class SecurityHeaders
{
    // Strict outside development; Vite's dev server needs inline scripts for hot reload.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        var strict = !app.Environment.IsDevelopment();

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (strict)
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }

            await next(context);
        });
    }
}

internal static class ApiAntiforgery
{
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Every state-changing <c>/api</c> request must echo the anti-forgery token (constitution VI).</summary>
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !IsSafe(context.Request.Method))
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(context))
                {
                    await Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Missing or invalid anti-forgery token.",
                            type: "antiforgery")
                        .ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
