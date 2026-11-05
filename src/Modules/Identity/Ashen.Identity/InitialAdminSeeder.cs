using Ashen.BuildingBlocks.Data;
using Ashen.Identity.Contracts;
using Ashen.Identity.Domain;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashen.Identity;

internal sealed class InitialAdminOptions
{
    public const string SectionName = "Ashen:InitialAdmin";

    /// <summary>The account with this email is made an Admin (FR-014). Optional.</summary>
    public string? Email { get; set; }
}

/// <summary>Gives the configured initial admin the Admin role, at start-up and when they first register.</summary>
internal sealed partial class InitialAdminSeeder(
    UserManager<Account> userManager,
    IOptions<InitialAdminOptions> options,
    ILogger<InitialAdminSeeder> logger) : IModuleSeeder
{
    public async Task Seed(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Email))
        {
            return;
        }

        var account = await userManager.FindByEmailAsync(options.Value.Email);
        if (account is not null)
        {
            await EnsureAdmin(account);
        }
    }

    /// <summary>Call after creating an account; promotes it only if it is the configured initial admin.</summary>
    public async Task EnsureAdmin(Account account)
    {
        var configured = options.Value.Email;
        if (string.IsNullOrWhiteSpace(configured)
            || !string.Equals(account.Email, configured, StringComparison.OrdinalIgnoreCase)
            || await userManager.IsInRoleAsync(account, Roles.Admin))
        {
            return;
        }

        var result = await userManager.AddToRoleAsync(account, Roles.Admin);
        if (result.Succeeded)
        {
            LogInitialAdminGranted(account.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Initial admin role granted to account {AccountId}")]
    private partial void LogInitialAdminGranted(Guid accountId);
}
