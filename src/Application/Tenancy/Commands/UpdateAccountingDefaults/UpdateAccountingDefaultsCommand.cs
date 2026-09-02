using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;

/// <summary>
/// Sets TenantSettings' Phase 5 (Sales), Phase 6 (Purchase), and Phase 7 (Inventory) fallback GL
/// accounts (see TenantSettings.SetAccountingDefaults/SetInventoryDefaults' doc comments) --
/// deliberately its own narrow command rather than a full TenantSettings editor, which doesn't
/// exist yet (roadmap Phase 2 left it for "later").
/// </summary>
public sealed record UpdateAccountingDefaultsCommand(
    Guid OrganizationId,
    Guid? DefaultSalesAccountId,
    Guid? DefaultAccountsReceivableId,
    Guid? DefaultVatPayableAccountId,
    Guid? DefaultPurchaseAccountId,
    Guid? DefaultAccountsPayableId,
    Guid? DefaultVatReceivableAccountId,
    Guid? DefaultTdsPayableAccountId,
    Guid? DefaultInventoryAccountId,
    Guid? DefaultCogsAccountId,
    Guid? DefaultInventoryAdjustmentAccountId,
    Guid? DefaultProductionCostAccountId)
    : IRequest<UpdateAccountingDefaultsResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountingDefaultsManage;
}

public sealed record UpdateAccountingDefaultsResult(
    Guid? DefaultSalesAccountId,
    Guid? DefaultAccountsReceivableId,
    Guid? DefaultVatPayableAccountId,
    Guid? DefaultPurchaseAccountId,
    Guid? DefaultAccountsPayableId,
    Guid? DefaultVatReceivableAccountId,
    Guid? DefaultTdsPayableAccountId,
    Guid? DefaultInventoryAccountId,
    Guid? DefaultCogsAccountId,
    Guid? DefaultInventoryAdjustmentAccountId,
    Guid? DefaultProductionCostAccountId);
