using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetDefaultCustomTemplate;

/// <summary>Same "move the single checkmark" shape as SetDefaultPrintingTemplateCommand, scoped to
/// (OrganizationId, Type) instead of (OrganizationId, DocumentType).</summary>
public sealed record SetDefaultCustomTemplateCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomTemplateManage;
}
