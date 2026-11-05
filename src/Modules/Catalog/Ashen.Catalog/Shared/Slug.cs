using System.Text.RegularExpressions;

namespace Ashen.Catalog.Shared;

internal static partial class Slug
{
    /// <summary>"Black Hoodie (2026)" → "black-hoodie-2026".</summary>
    public static string From(string name) => NonAlphanumeric().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
