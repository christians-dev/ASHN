using System.Security.Claims;

using Ashen.Identity.Domain;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Ashen.Identity;

/// <summary>Adds the display name to the sign-in cookie so it is available without a database call.</summary>
internal sealed class AccountClaimsPrincipalFactory(
    UserManager<Account> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Account, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Account user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AccountClaims.DisplayName, user.DisplayName));
        return identity;
    }
}
