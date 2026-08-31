using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetDefaultPrintingTemplate;

/// <summary>
/// Marks one PrintingTemplate as the default for its own DocumentType, clearing any other row
/// that was previously default in that same (OrganizationId, DocumentType) group -- mirrors the
/// reference product's gallery, where selecting a new thumbnail moves the single checkmark.
/// </summary>
public sealed record SetDefaultPrintingTemplateCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}
