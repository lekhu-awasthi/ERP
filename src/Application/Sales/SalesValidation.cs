using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>Shared existence checks reused by every Sales/Payments Create/Update handler --
/// mirrors Accounting.AccountingValidation's precedent.</summary>
internal static class SalesValidation
{
    public static async Task EnsureContactExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, ContactType expectedType, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == expectedType, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException($"{expectedType} not found.");
        }
    }

    public static async Task EnsureProductsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var existingCount = await db.Products.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more products were not found.");
        }
    }

    public static async Task EnsureWarehouseExistsAsync(
        IAppDbContext db, Guid organizationId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var exists = await db.Warehouses.AnyAsync(
            x => x.Id == warehouseId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }
}
