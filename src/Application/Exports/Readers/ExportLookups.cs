using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 78 -- the names a sheet prints for an id its rows carry: a unit, a billing location, an account, a cost term.
///
/// <para><b>Resolved in memory, from the tenant's whole list, not joined into each query.</b> Each list is a tenant's
/// configuration (a few units, one or a handful of locations, a chart of accounts), bounded by the tenant and not by
/// the period, so loading it once is cheaper than a join in every one of a reader's queries. It also keeps each query's
/// row a plain value, which is what lets phase 77's readers merge their pages in memory. Handing the page's ids back to
/// SQL instead would be phase 34c's <c>OPENJSON</c> as long as the page (50,000 rows).</para>
/// </summary>
internal static class ExportLookups
{
    /// <summary>A unit's full name ("Box", "Kilogram"), as the Sales Register prints it.</summary>
    public static Task<Dictionary<Guid, string>> UnitsAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.UnitsOfMeasurement.Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    public static Task<Dictionary<Guid, string>> LocationsAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.BillingLocations.Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    /// <summary>An account as the Chart of Accounts sheet identifies it: code and name.</summary>
    public static Task<Dictionary<Guid, string>> AccountsAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Accounts.Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Code + " " + x.Name, ct);

    public static Task<Dictionary<Guid, string>> CostTermsAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.CostTerms.Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    /// <summary>Phase 80 -- a warehouse's name, for the goods-note and production sheets.</summary>
    public static Task<Dictionary<Guid, string>> WarehousesAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Warehouses.Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    /// <summary>The name for an id, or nothing when the row has none.</summary>
    public static string? NameOf(this IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } key ? names.GetValueOrDefault(key) : null;
}
