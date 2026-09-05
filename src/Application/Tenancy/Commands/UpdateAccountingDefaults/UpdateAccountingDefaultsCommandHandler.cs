using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;

public sealed class UpdateAccountingDefaultsCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateAccountingDefaultsCommand, UpdateAccountingDefaultsResult>
{
    public async Task<UpdateAccountingDefaultsResult> Handle(
        UpdateAccountingDefaultsCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var accountIds = new[]
            {
                request.DefaultSalesAccountId,
                request.DefaultAccountsReceivableId,
                request.DefaultVatPayableAccountId,
                request.DefaultPurchaseAccountId,
                request.DefaultAccountsPayableId,
                request.DefaultVatReceivableAccountId,
                request.DefaultTdsPayableAccountId,
                request.DefaultInventoryAccountId,
                request.DefaultCogsAccountId,
                request.DefaultInventoryAdjustmentAccountId,
                request.DefaultProductionCostAccountId,
                request.DefaultForexGainAccountId,
                request.DefaultForexLossAccountId,
                request.DefaultLandedCostClearingAccountId,
            }
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (accountIds.Count > 0)
        {
            var existingCount = await db.Accounts.CountAsync(
                x => x.OrganizationId == request.OrganizationId && accountIds.Contains(x.Id), cancellationToken);

            if (existingCount != accountIds.Count)
            {
                throw new NotFoundException("One or more accounts were not found.");
            }
        }

        settings.SetAccountingDefaults(
            request.DefaultSalesAccountId,
            request.DefaultAccountsReceivableId,
            request.DefaultVatPayableAccountId,
            request.DefaultPurchaseAccountId,
            request.DefaultAccountsPayableId,
            request.DefaultVatReceivableAccountId,
            request.DefaultTdsPayableAccountId,
            request.DefaultForexGainAccountId,
            request.DefaultForexLossAccountId);
        settings.SetInventoryDefaults(
            request.DefaultInventoryAccountId,
            request.DefaultCogsAccountId,
            request.DefaultInventoryAdjustmentAccountId,
            request.DefaultProductionCostAccountId,
            request.DefaultLandedCostClearingAccountId);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateAccountingDefaultsResult(
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
            settings.DefaultProductionCostAccountId,
            settings.DefaultForexGainAccountId,
            settings.DefaultForexLossAccountId,
            settings.DefaultLandedCostClearingAccountId);
    }
}
