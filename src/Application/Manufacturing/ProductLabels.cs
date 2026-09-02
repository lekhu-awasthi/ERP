using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing;

/// <summary>Name/code/unit for a set of product ids, in one round trip. Every manufacturing detail
/// and report handler needs the same three fields for a mixed bag of finished goods, raw materials
/// and by-products, and doing it per collection would mean three queries where one will do.</summary>
internal static class ProductLabels
{
    public sealed record Label(string Name, string Code, string? UnitName);

    public static async Task<Dictionary<Guid, Label>> LoadAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var rows = await (
            from product in db.Products
            join unit in db.UnitsOfMeasurement on product.PrimaryUnitId equals unit.Id into units
            from unit in units.DefaultIfEmpty()
            where product.OrganizationId == organizationId && distinctIds.Contains(product.Id)
            select new { product.Id, product.Name, product.Code, UnitName = unit != null ? unit.Name : null })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => new Label(x.Name, x.Code, x.UnitName));
    }
}
