using Ashen.BuildingBlocks;
using Ashen.BuildingBlocks.Data;
using Ashen.Pricing.Domain;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Pricing.Data;

/// <summary>Launch configuration: United States, US dollars, Illinois 6.25%, $5.00 flat shipping (FR-031).</summary>
internal sealed class PricingSeeder(PricingDbContext db) : IModuleSeeder
{
    public async Task Seed(CancellationToken cancellationToken)
    {
        if (await db.Regions.AnyAsync(cancellationToken))
        {
            return;
        }

        var us = new RegionSetting("US", "USD", "en-US", ["US"], UsStates, UsZipCode);
        db.Regions.Add(us);
        db.TaxRates.Add(new TaxRate(us.Id, "IL", 0.0625m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        db.StoreSettings.Add(new StoreSettings(new Money(5.00m, "USD")));

        await db.SaveChangesAsync(cancellationToken);
    }

    public const string UsZipCode = @"^\d{5}(-\d{4})?$";

    private static readonly Subdivision[] UsStates =
    [
        new("AL", "Alabama"), new("AK", "Alaska"), new("AZ", "Arizona"), new("AR", "Arkansas"),
        new("CA", "California"), new("CO", "Colorado"), new("CT", "Connecticut"), new("DE", "Delaware"),
        new("DC", "District of Columbia"), new("FL", "Florida"), new("GA", "Georgia"), new("HI", "Hawaii"),
        new("ID", "Idaho"), new("IL", "Illinois"), new("IN", "Indiana"), new("IA", "Iowa"),
        new("KS", "Kansas"), new("KY", "Kentucky"), new("LA", "Louisiana"), new("ME", "Maine"),
        new("MD", "Maryland"), new("MA", "Massachusetts"), new("MI", "Michigan"), new("MN", "Minnesota"),
        new("MS", "Mississippi"), new("MO", "Missouri"), new("MT", "Montana"), new("NE", "Nebraska"),
        new("NV", "Nevada"), new("NH", "New Hampshire"), new("NJ", "New Jersey"), new("NM", "New Mexico"),
        new("NY", "New York"), new("NC", "North Carolina"), new("ND", "North Dakota"), new("OH", "Ohio"),
        new("OK", "Oklahoma"), new("OR", "Oregon"), new("PA", "Pennsylvania"), new("RI", "Rhode Island"),
        new("SC", "South Carolina"), new("SD", "South Dakota"), new("TN", "Tennessee"), new("TX", "Texas"),
        new("UT", "Utah"), new("VT", "Vermont"), new("VA", "Virginia"), new("WA", "Washington"),
        new("WV", "West Virginia"), new("WI", "Wisconsin"), new("WY", "Wyoming"),
    ];
}
