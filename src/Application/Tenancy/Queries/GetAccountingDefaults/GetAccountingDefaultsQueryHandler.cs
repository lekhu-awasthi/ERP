using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;

public sealed class GetAccountingDefaultsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAccountingDefaultsQuery, GetAccountingDefaultsDto>
{
    public async Task<GetAccountingDefaultsDto> Handle(GetAccountingDefaultsQuery request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        return new GetAccountingDefaultsDto(
            settings.DefaultSalesAccountId,
            settings.DefaultAccountsReceivableId,
            settings.DefaultVatPayableAccountId,
            settings.DefaultPurchaseAccountId,
            settings.DefaultAccountsPayableId,
            settings.DefaultVatReceivableAccountId,
            settings.DefaultTdsPayableAccountId,
            settings.DefaultInventoryAccountId,
            settings.DefaultCogsAccountId,
            settings.DefaultInventoryAdjustmentAccountId,
            settings.DefaultProductionCostAccountId);
    }
}
