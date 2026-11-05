using Ashen.Customers.Contracts;
using Ashen.Customers.Data;

using Microsoft.EntityFrameworkCore;

namespace Ashen.Customers;

internal sealed class CustomersModuleApi(CustomersDbContext db) : ICustomersModule
{
    public Task<AddressSnapshot?> GetAddress(Guid customerId, Guid addressId, CancellationToken cancellationToken = default) =>
        db.Profiles.AsNoTracking()
            .Where(p => p.Id == customerId)
            .SelectMany(p => p.Addresses)
            .Where(a => a.Id == addressId)
            .Select(a => new AddressSnapshot(a.RecipientName, a.Line1, a.Line2, a.City, a.StateCode, a.PostalCode, a.CountryCode, a.Phone))
            .SingleOrDefaultAsync(cancellationToken);
}
