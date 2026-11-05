using Ashen.BuildingBlocks.Data;
using Ashen.Identity.Domain;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ashen.Identity.Data;

/// <summary>
/// Identity's tables in the <c>identity</c> schema. It must inherit Identity's base class, so it cannot
/// inherit <see cref="ModuleDbContext"/> and applies the shared conventions itself.
/// </summary>
internal sealed class AshenIdentityDbContext(DbContextOptions<AshenIdentityDbContext> options)
    : IdentityDbContext<Account, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    /// <summary>
    /// The keys that encrypt sign-in cookies, anti-forgery tokens and reset links, shared by every app instance so a
    /// redeploy doesn't sign everyone out and any instance can read any cookie.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Fixed ids so the role seed is stable across migrations.
    internal static readonly Guid CustomerRoleId = Guid.Parse("0193f1a0-0000-7000-8000-000000000001");
    internal static readonly Guid AdminRoleId = Guid.Parse("0193f1a0-0000-7000-8000-000000000002");

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyModuleConventions(Schema);

        builder.Entity<Account>(account =>
        {
            account.ToTable("users");
            account.Property(a => a.DisplayName).HasMaxLength(100);
            account.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.Entity<IdentityRole<Guid>>().HasData(
            new IdentityRole<Guid> { Id = CustomerRoleId, Name = Contracts.Roles.Customer, NormalizedName = "CUSTOMER", ConcurrencyStamp = "customer" },
            new IdentityRole<Guid> { Id = AdminRoleId, Name = Contracts.Roles.Admin, NormalizedName = "ADMIN", ConcurrencyStamp = "admin" });
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the web host.</summary>
internal sealed class AshenIdentityDbContextFactory : IDesignTimeDbContextFactory<AshenIdentityDbContext>
{
    public AshenIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AshenIdentityDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", AshenIdentityDbContext.Schema);
        return new AshenIdentityDbContext(options.Options);
    }
}
