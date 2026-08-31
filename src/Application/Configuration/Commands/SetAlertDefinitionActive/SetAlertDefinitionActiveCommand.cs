using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetAlertDefinitionActive;

/// <summary>
/// Backs the reference product's row-level "Mark As Inactive" action, which sits in the Alert
/// Scheduler grid's own kebab menu alongside Edit and Delete (confirmed live, Phase 20e).
///
/// <para>A dedicated single-field command rather than "call Update with everything else unchanged":
/// the row action has no form open, so a read-modify-write round trip would happily clobber an edit
/// made from another tab between the read and the write. Same reasoning that gave SetDefault* their
/// own commands in Phase 20d.</para>
/// </summary>
public sealed record SetAlertDefinitionActiveCommand(Guid OrganizationId, Guid Id, bool IsActive)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AlertDefinitionManage;
}
