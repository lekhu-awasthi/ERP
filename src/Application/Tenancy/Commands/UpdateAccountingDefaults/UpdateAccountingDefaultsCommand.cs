using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;

/// <summary>
/// Sets TenantSettings' three Phase 5 fallback GL accounts (see TenantSettings.SetAccountingDefaults'
/// doc comment) -- deliberately its own narrow command rather than a full TenantSettings editor,
/// which doesn't exist yet (roadmap Phase 2 left it for "later").
/// </summary>
public sealed record UpdateAccountingDefaultsCommand(
    Guid OrganizationId, Guid? DefaultSalesAccountId, Guid? DefaultAccountsReceivableId, Guid? DefaultVatPayableAccountId)
    : IRequest<UpdateAccountingDefaultsResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountingDefaultsManage;
}

public sealed record UpdateAccountingDefaultsResult(
    Guid? DefaultSalesAccountId, Guid? DefaultAccountsReceivableId, Guid? DefaultVatPayableAccountId);
