using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.ListLookups;

/// <summary>
/// Generic list query shared by every Configuration lookup type (CreditTerm, PaymentMode,
/// CustomStatus, ReportingTagCategory, ReportingTagOption -- architecture-spec.md §4.10). Real
/// genericity pays off here (unlike Create/Update) because listing needs nothing beyond
/// Id/OrganizationId, identical across every lookup type. Registered explicitly per closed
/// TLookup in Application/DependencyInjection.cs -- MediatR's assembly scan can't discover a
/// handler whose service type response is derived from its own generic parameter (the same
/// reason ListLookupsQueryValidator&lt;TLookup&gt; is registered explicitly there too, not via
/// AddValidatorsFromAssembly).
///
/// Page/PageSize (Phase 16c) default large enough that every lookup screen -- bounded master
/// data (credit terms, payment modes, TDS types, task types, lead sources, deal stages,
/// warehouses, account/contact/product groups, UoMs, custom statuses, reporting tags) -- keeps
/// its pre-existing "show everything" UX without callers needing to opt in; no Angular list page
/// backed by this query gets a visible pager (phase-16c-status.md's scope decision -- these
/// tables never realistically approach NFR-5.1's "tens of thousands" framing).
/// </summary>
public sealed record ListLookupsQuery<TLookup>(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<PagedResult<TLookup>>, IRequirePermission, IOrganizationScoped
    where TLookup : class, ITenantLookupEntity
{
    public string PermissionKey => LookupPermissionKeys.ViewKeyFor<TLookup>();
}
