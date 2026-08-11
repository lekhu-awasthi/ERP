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
/// handler whose service type response is derived from its own generic parameter.
/// </summary>
public sealed record ListLookupsQuery<TLookup>(Guid OrganizationId)
    : IRequest<IReadOnlyList<TLookup>>, IRequirePermission, IOrganizationScoped
    where TLookup : class, ITenantLookupEntity
{
    public string PermissionKey => LookupPermissionKeys.ViewKeyFor<TLookup>();
}
