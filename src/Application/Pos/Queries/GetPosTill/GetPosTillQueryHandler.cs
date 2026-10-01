using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Domain.Pos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosTill;

public sealed class GetPosTillQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPosTillQuery, PosTillDto>
{
    public async Task<PosTillDto> Handle(GetPosTillQuery request, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var location = till.Location;
        var settings = till.Settings;

        var isVatRegistered = await db.Organizations
            .Where(x => x.Id == request.OrganizationId)
            .Select(x => x.IsVatRegistered)
            .SingleAsync(cancellationToken);

        var warehouseName = location.WarehouseId is { } warehouseId
            ? await db.Warehouses
                .Where(x => x.Id == warehouseId && x.OrganizationId == request.OrganizationId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var walkIn = await db.Contacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsWalkInCustomer)
            .Select(x => new PosWalkInCustomerDto(x.Id, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken);

        // Exactly the modes CreatePosSaleCommandHandler accepts as a tender here: linked, active, and
        // naming an account. A mode failing any of those would be refused at Pay, so it is not offered.
        // Ordered in memory by the enum (Cash first), not by the stored string, which sorts Card first.
        var paymentModes = (await (
                    from link in db.PosLocationPaymentModes
                    join mode in db.PaymentModes on link.PaymentModeId equals mode.Id
                    where link.OrganizationId == request.OrganizationId && link.BillingLocationId == location.Id
                          && mode.OrganizationId == request.OrganizationId && mode.IsActive && mode.AccountId != null
                    select new PosTillPaymentModeDto(mode.Id, mode.Name, mode.Kind))
                .ToListAsync(cancellationToken))
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The grid's tabs: categories holding at least one product the till would sell here -- the
        // same four conditions ListPosProductsQueryHandler filters on, so a tab is never empty.
        var categories = await db.ProductCategories
            .AsNoTracking()
            .Where(c => c.OrganizationId == request.OrganizationId)
            .Where(c => db.Products.Any(p =>
                p.OrganizationId == request.OrganizationId && p.CategoryId == c.Id
                && p.AvailableForSale && p.IsActive && !p.HasVariants
                && (p.Locations.Count == 0 || p.Locations.Any(l => l.LocationId == location.Id))))
            .OrderBy(c => c.Name)
            .Select(c => new PosTillCategoryDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        var canSellOnCredit = await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceApprove, location.Id,
            cancellationToken);

        return new PosTillDto(
            location.Id,
            location.Code,
            location.Name,
            location.PosMode,
            PosTabs.For(location.PosMode),
            settings.EffectiveDefaultTab(location.PosMode),
            settings.ServiceChargeEnabled,
            settings.ServiceChargeRate,
            settings.RoundOffEnabled,
            settings.CashVerificationRequired,
            settings.Denominations,
            settings.PrintInvoice,
            settings.AbbreviatedTaxInvoiceEnabled,
            isVatRegistered,
            location.WarehouseId,
            warehouseName,
            walkIn,
            paymentModes,
            categories,
            canSellOnCredit);
    }
}
