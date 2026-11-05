using Ashen.BuildingBlocks;

using Microsoft.AspNetCore.Identity;

namespace Ashen.Identity.Shared;

internal static class IdentityProblems
{
    /// <summary>Turns ASP.NET Identity errors into a validation error keyed by the form field they belong to.</summary>
    public static Error ToValidationError(this IdentityResult result)
    {
        var fields = result.Errors
            .GroupBy(e => FieldFor(e.Code))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return Error.Validation("identity.invalid", "Please fix the highlighted fields.", fields);
    }

    private static string FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        _ when code.Contains("Email", StringComparison.Ordinal) || code.Contains("UserName", StringComparison.Ordinal) => "email",
        "InvalidToken" => "token",
        _ => "",
    };

    /// <summary>Only paths on this site: blocks open redirects such as <c>//evil.example</c> and <c>/\evil.example</c>.</summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
