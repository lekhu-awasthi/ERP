using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateBillingLocation;

public sealed class CreateBillingLocationCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateBillingLocationCommand, CreateBillingLocationResult>
{
    public async Task<CreateBillingLocationResult> Handle(
        CreateBillingLocationCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();

        var codeTaken = await db.BillingLocations.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Code == code, cancellationToken);

        if (codeTaken)
        {
            throw new ConflictException($"A billing location with code '{code}' already exists.");
        }

        if (request.WarehouseId is { } warehouseId)
        {
            var warehouseExists = await db.Warehouses.AnyAsync(
                x => x.Id == warehouseId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!warehouseExists)
            {
                throw new NotFoundException("Warehouse not found.");
            }
        }

        await EnforceMultipleLocationsEntitlementAsync(request.OrganizationId, cancellationToken);

        var location = BillingLocation.Create(
            request.OrganizationId, code, request.Name, request.Address, request.WarehouseId);

        db.BillingLocations.Add(location);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBillingLocationResult(location.Id, location.Code, location.Name);
    }

    /// <summary>
    /// Phase 32, and <b>the third instance of phase-20f Decision #4's shape</b>: the entitlement is a
    /// cap on the location list, not a block on documents. Every Organization is seeded with the
    /// HeadOffice row at creation, so a tenant without MultipleLocations has exactly one location and
    /// is capped there; the <i>second</i> location is what the entitlement buys, precisely as the
    /// second warehouse (20f) and the second currency (28) are.
    ///
    /// <para>This is why <b>no document command in this phase implements
    /// <see cref="Common.Security.IRequireFeature"/></b>, and the argument is the same one phase 28
    /// made for currency, strengthened by a live reading: a document's location picker is populated
    /// from the tenant's own active location list, so with a one-entry list the whole surface
    /// degenerates to "HeadOffice, fixed" by itself. Gating the document commands as well would be a
    /// second enforcement of one rule, and the one that breaks first when the two disagree.</para>
    ///
    /// <para>Conditional on the existing count, so -- like both earlier caps -- it cannot ride
    /// <c>FeatureGateBehavior</c>'s marker interface and lives here in the handler. Fails closed on a
    /// missing subscription row, same as that behavior.</para>
    /// </summary>
    private async Task EnforceMultipleLocationsEntitlementAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var existingCount = await db.BillingLocations.CountAsync(
            x => x.OrganizationId == organizationId, cancellationToken);

        if (existingCount == 0)
        {
            return;
        }

        var enabled = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (bool?)x.MultipleLocationsEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        if (enabled != true)
        {
            throw new FeatureNotEnabledException(
                "This organization does not have the Multiple Locations feature enabled, so it is limited to "
                + "its HeadOffice location only. Accounting Features are chosen when the organization is created "
                + "and cannot be changed afterwards.");
        }
    }
}
