using Microsoft.AspNetCore.Identity;

namespace Ashen.Identity.Domain;

internal enum AccountStatus
{
    Active,
    Deleted,
}

/// <summary>A person who can sign in. Built on ASP.NET Core Identity's user.</summary>
internal sealed class Account : IdentityUser<Guid>
{
    public Account()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = "";
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}
