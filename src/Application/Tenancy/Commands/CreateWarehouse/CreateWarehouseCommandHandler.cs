using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateWarehouse;

public sealed class CreateWarehouseCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateWarehouseCommand, CreateWarehouseResult>
{
    public async Task<CreateWarehouseResult> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        await EnforceWarehouseEntitlementAsync(request.OrganizationId, cancellationToken);

        var nameExists = await db.Warehouses.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A warehouse named '{request.Name}' already exists.");
        }

        var warehouse = Warehouse.Create(request.OrganizationId, request.Name);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateWarehouseResult(warehouse.Id, warehouse.Name);
    }

    /// <summary>
    /// Phase 20f (FR-2.6). The MultipleWarehouses entitlement is the one gate in this codebase
    /// that is *conditional* rather than on/off, so it can't ride FeatureGateBehavior: a tenant
    /// who never opted in still needs exactly one warehouse, because Invoice and PurchaseBill
    /// both require a WarehouseId and nothing seeds a default one at Organization creation.
    /// Gating warehouse creation outright would leave such a tenant permanently unable to
    /// invoice. So the rule is a cap, not a block -- the *second* warehouse is what the
    /// entitlement buys, matching the reference product, whose Features tab calls this
    /// "Multiple Warehouse" and whose subscription screen carries it as warehouseEnabled.
    ///
    /// Stating the cap this way also leaves Organizations created before this phase (which have
    /// zero warehouses) able to create their first one, with no backfill migration.
    /// </summary>
    private async Task EnforceWarehouseEntitlementAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var existingCount = await db.Warehouses.CountAsync(x => x.OrganizationId == organizationId, cancellationToken);

        if (existingCount == 0)
        {
            return;
        }

        var enabled = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (bool?)x.MultipleWarehousesEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        // Fail closed on a missing subscription row, same as FeatureGateBehavior.
        if (enabled != true)
        {
            throw new FeatureNotEnabledException(
                "This organization does not have the Multiple Warehouses feature enabled, so it is limited to a " +
                "single warehouse. Accounting Features are chosen when the organization is created and cannot be " +
                "changed afterwards.");
        }
    }
}
