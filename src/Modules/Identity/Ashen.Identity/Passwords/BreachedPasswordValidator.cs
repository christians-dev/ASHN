using Ashen.Identity.Domain;

using Microsoft.AspNetCore.Identity;

namespace Ashen.Identity.Passwords;

/// <summary>Rejects passwords known to be commonly breached (FR-010). No network call: the list ships with the app.</summary>
internal sealed class BreachedPasswordValidator : IPasswordValidator<Account>
{
    public const string ErrorCode = "PasswordCommonlyBreached";

    private static readonly Lazy<HashSet<string>> Common = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<Account> manager, Account user, string? password) =>
        Task.FromResult(password is not null && Common.Value.Contains(password)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = ErrorCode,
                Description = "This password appears in lists of breached passwords. Please choose another.",
            })
            : IdentityResult.Success);

    private static HashSet<string> Load()
    {
        using var stream = typeof(BreachedPasswordValidator).Assembly.GetManifestResourceStream("Ashen.Identity.common-passwords.txt")
            ?? throw new InvalidOperationException("The common-passwords list is missing from the Identity assembly.");
        using var reader = new StreamReader(stream);

        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith("# ", StringComparison.Ordinal)) // some real passwords start with '#'
            {
                passwords.Add(line.Trim());
            }
        }

        return passwords;
    }
}
