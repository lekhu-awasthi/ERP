using ErpApp.Application.Common.Persistence;

namespace ErpApp.Application.Catalog;

/// <summary>
/// Phase 95 -- which products a location may offer, as a set the database computes once. Phase 36's rule: a product
/// is available at a location when it is restricted to no location at all, or to that one among others.
///
/// <para><b>Why a set.</b> Written per product (<c>Locations.Count == 0 || Locations.Any(...)</c>, or its
/// <c>!Any()</c> form), the rule is two correlated subqueries, and SQL Server ran them as a seek per product: on a
/// 20,001-product tenant with one location row, the till grid's page count read <c>ProductLocations</c> 80,004 times
/// for 84,465 logical reads in all. The same rule as "not among the products restricted elsewhere" is one pass over
/// the location rows and an anti-join: 4,463 (tools/scale/probe-phase95-*.csv).</para>
///
/// <para><b>Not everywhere.</b> The till's and the restaurant's category tabs ask whether <i>any</i> product of a
/// category is available, and that EXISTS stops at the first one, so the per-product form costs them 10 reads. Given this
/// set, the plan read every product (4,465). They keep the per-product form; measure before moving a reader onto this.
/// </para>
///
/// <para>The location rows carry no <c>OrganizationId</c>; the ids they name are another tenant's products or none of
/// the caller's, so reading every row decides nothing wrong, and the caller's own <c>OrganizationId</c> filter on the
/// products stays the tenant boundary.</para>
/// </summary>
public static class ProductLocationAvailability
{
    /// <summary>The products restricted to one or more locations, none of which is <paramref name="locationId"/>:
    /// filter with <c>!restricted.Contains(product.Id)</c>.</summary>
    public static IQueryable<Guid> RestrictedAwayFrom(IAppDbContext db, Guid locationId) =>
        db.ProductLocations
            .GroupBy(x => x.ProductId)
            .Where(g => g.Count(x => x.LocationId == locationId) == 0)
            .Select(g => g.Key);
}
