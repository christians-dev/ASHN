using System.Text.RegularExpressions;

using Ashen.BuildingBlocks;
using Ashen.Pricing.Contracts;

namespace Ashen.Customers.Domain;

/// <summary>
/// Checks an address against the active region's rules (FR-017): which countries the store ships to, valid
/// states and the postal-code format all come from region data, never from code (constitution III).
/// </summary>
internal static class AddressValidator
{
    public static Error? Validate(AddressDetails address, RegionInfo region)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(string field, string? value, string label, int max = 100)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[field] = [$"Enter {label}."];
            }
            else if (value.Trim().Length > max)
            {
                errors[field] = [$"Keep {label} under {max} characters."];
            }
        }

        Require("recipientName", address.RecipientName, "the recipient's name");
        Require("line1", address.Line1, "the street address");
        Require("city", address.City, "the city");
        Require("phone", address.Phone, "a phone number", max: 30);
        if (address.Line2?.Trim().Length > 100)
        {
            errors["line2"] = ["Keep the second address line under 100 characters."];
        }

        var country = address.CountryCode.Trim().ToUpperInvariant();
        if (!region.ShippingCountries.Contains(country))
        {
            errors["countryCode"] = [$"We currently ship to {string.Join(", ", region.ShippingCountries)} only."];
        }
        else if (country == region.CountryCode)
        {
            var state = address.StateCode.Trim().ToUpperInvariant();
            if (!region.States.Any(s => s.Code == state))
            {
                errors["stateCode"] = ["Choose a state."];
            }

            var postalCode = address.PostalCode.Trim();
            if (postalCode.Length == 0 || (region.PostalCodePattern.Length > 0
                && !Regex.IsMatch(postalCode, region.PostalCodePattern, RegexOptions.None, TimeSpan.FromMilliseconds(100))))
            {
                errors["postalCode"] = ["Enter a valid postal code."];
            }
        }

        return errors.Count == 0 ? null : Error.Validation("address.invalid", "Please fix the highlighted fields.", errors);
    }
}
