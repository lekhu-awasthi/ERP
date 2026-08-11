using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.DeleteLookup;

/// <summary>
/// Generic delete command shared by every Configuration lookup type -- see ListLookupsQuery's
/// remarks. Implements IRequest&lt;Unit&gt; explicitly (not the bare IRequest marker) so the
/// explicit DI registration in Application/DependencyInjection.cs has an unambiguous
/// IRequestHandler&lt;,&gt; service type to target.
/// </summary>
public sealed record DeleteLookupCommand<TLookup>(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
    where TLookup : class, ITenantLookupEntity
{
    public string PermissionKey => LookupPermissionKeys.ManageKeyFor<TLookup>();
}
