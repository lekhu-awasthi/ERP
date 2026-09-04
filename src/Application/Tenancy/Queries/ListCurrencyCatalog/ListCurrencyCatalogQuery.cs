using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListCurrencyCatalog;

/// <summary>
/// The standard catalog the "Add New Currency" picker offers, minus whatever this tenant has
/// already activated -- the live dialog's own behaviour, and the reason it renders empty on a
/// tenant that has activated everything.
///
/// <para>Organization-scoped (and therefore permission-gated) even though the catalog itself is
/// static product data, because subtracting the already-activated codes requires the tenant. Every
/// <c>IOrganizationScoped</c> request must implement <c>IRequirePermission</c> -- phase 12's
/// rule -- and CurrencyView is the honest key: this is the picker behind the currency list.</para>
/// </summary>
public sealed record ListCurrencyCatalogQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<CurrencyCatalogEntryDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CurrencyView;
}

public sealed record CurrencyCatalogEntryDto(string Code, string Name, string Symbol, bool AlreadyActivated)
{
    public static CurrencyCatalogEntryDto From(CurrencyCatalogEntry entry, bool alreadyActivated) =>
        new(entry.Code, entry.Name, entry.Symbol, alreadyActivated);
}
