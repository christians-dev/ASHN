using Ashen.BuildingBlocks;
using Ashen.Customers.Domain;
using Ashen.Pricing.Contracts;

namespace Ashen.UnitTests.Customers;

public class AddressTests
{
    private static readonly RegionInfo Us = new("US", "USD", "en-US", ["US"], [new("IL", "Illinois"), new("TX", "Texas")], @"^\d{5}(-\d{4})?$");

    private static AddressDetails Address(string recipient = "Sam Lee", string state = "IL", string zip = "60601", string country = "US") =>
        new(recipient, "1 Main St", null, "Chicago", state, zip, country, "+1 312 555 0100");

    [Fact]
    public void First_address_becomes_the_default()
    {
        var profile = new CustomerProfile(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch);

        var first = profile.AddAddress(Address("A"));
        var second = profile.AddAddress(Address("B"));

        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);
    }

    [Fact]
    public void Exactly_one_address_is_default()
    {
        var profile = new CustomerProfile(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch);
        profile.AddAddress(Address("A"));
        var second = profile.AddAddress(Address("B"));

        Assert.True(profile.SetDefault(second.Id).IsSuccess);

        Assert.Single(profile.Addresses, a => a.IsDefault);
        Assert.True(second.IsDefault);
    }

    [Fact]
    public void Deleting_the_default_promotes_another()
    {
        var profile = new CustomerProfile(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch);
        var first = profile.AddAddress(Address("A"));
        var second = profile.AddAddress(Address("B"));

        Assert.True(profile.RemoveAddress(first.Id).IsSuccess);

        Assert.True(second.IsDefault);
        Assert.Equal(ErrorKind.NotFound, profile.RemoveAddress(first.Id).Error?.Kind);
    }

    [Fact]
    public void Valid_us_address_passes()
    {
        Assert.Null(AddressValidator.Validate(Address(zip: "60601-1234"), Us));
    }

    [Theory]
    [InlineData("6060")]
    [InlineData("606011")]
    [InlineData("60601-12")]
    [InlineData("ABCDE")]
    public void Zip_must_match_the_region_pattern(string zip)
    {
        Assert.Contains("postalCode", AddressValidator.Validate(Address(zip: zip), Us)!.Fields!.Keys);
    }

    [Fact]
    public void State_must_be_one_of_the_region_states()
    {
        Assert.Contains("stateCode", AddressValidator.Validate(Address(state: "ZZ"), Us)!.Fields!.Keys);
    }

    [Fact]
    public void Country_must_be_one_the_store_ships_to()
    {
        Assert.Contains("countryCode", AddressValidator.Validate(Address(country: "CA"), Us)!.Fields!.Keys);
    }

    [Fact]
    public void Required_fields_are_flagged_together()
    {
        var fields = AddressValidator.Validate(new AddressDetails("", "", null, "", "", "", "US", ""), Us)!.Fields!.Keys;

        Assert.Equivalent(new[] { "recipientName", "line1", "city", "stateCode", "postalCode", "phone" }, fields);
    }
}
