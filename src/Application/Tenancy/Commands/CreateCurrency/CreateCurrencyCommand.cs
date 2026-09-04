using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateCurrency;

/// <summary>
/// Activates one currency from <see cref="ErpApp.Domain.Common.CurrencyCatalog"/> for this tenant
/// -- the "Add New Currency" dialog confirmed live on Organization &gt; Features 2026-09-04:
/// a Currency picker over the standard catalog, plus an editable Name and Symbol it pre-fills.
///
/// <para>Name/Symbol are optional here even though the live dialog marks them required, because
/// the picker always fills them: leaving them null means "take the catalog's own", which is what
/// the dialog does before the user touches anything.</para>
/// </summary>
public sealed record CreateCurrencyCommand(Guid OrganizationId, string Code, string? Name = null, string? Symbol = null)
    : IRequest<CreateCurrencyResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CurrencyManage;
}

public sealed record CreateCurrencyResult(Guid Id, string Code, string Name, string Symbol);
