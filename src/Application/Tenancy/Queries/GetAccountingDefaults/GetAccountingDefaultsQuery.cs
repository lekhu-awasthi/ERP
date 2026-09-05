using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;

/// <summary>
/// Phase 25 note: this query shipped implementing <i>neither</i> IRequirePermission nor
/// IOrganizationScoped, which meant AuthorizationBehavior never ran for it -- and since that
/// behavior is the only org-membership check in the pipeline (phase-12-status.md), any
/// authenticated user could read any tenant's accounting defaults by passing its id. Found while
/// adding DefaultProductionCostAccountId below; fixed here rather than left, because it is one
/// line and the file was already open. It reads the same tenant-wide control-plane settings its
/// own Update command writes, so it takes that command's key.
/// </summary>
public sealed record GetAccountingDefaultsQuery(Guid OrganizationId)
    : IRequest<GetAccountingDefaultsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountingDefaultsManage;
}

public sealed record GetAccountingDefaultsDto(
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
    Guid? DefaultProductionCostAccountId,
    Guid? DefaultForexGainAccountId,
    Guid? DefaultForexLossAccountId,
    Guid? DefaultLandedCostClearingAccountId);
