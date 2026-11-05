using Ashen.Identity.Domain;
using Ashen.Identity.Passwords;

namespace Ashen.UnitTests.Identity;

public class BreachedPasswordValidatorTests
{
    private readonly BreachedPasswordValidator _validator = new();

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("12345678")]
    [InlineData("Qwerty123")]
    public async Task Rejects_common_passwords_ignoring_case(string password)
    {
        var result = await _validator.ValidateAsync(null!, new Account(), password);

        Assert.False(result.Succeeded);
        Assert.Equal(BreachedPasswordValidator.ErrorCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Ashen-Fleece-1987!")]
    public async Task Accepts_uncommon_passwords(string password)
    {
        var result = await _validator.ValidateAsync(null!, new Account(), password);

        Assert.True(result.Succeeded);
    }
}
