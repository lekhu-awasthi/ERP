using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateCurrency;

/// <summary>Renames/re-symbols a tenant currency and toggles its active flag. Code is not
/// editable -- it is what every document stores (see Domain.Tenancy.Currency).</summary>
public sealed record UpdateCurrencyCommand(Guid OrganizationId, Guid Id, string Name, string Symbol, bool IsActive)
    : IRequest<UpdateCurrencyResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CurrencyManage;
}

public sealed record UpdateCurrencyResult(Guid Id, string Code, string Name, string Symbol, bool IsActive);
