using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplate;

public sealed record UpdatePrintingTemplateCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    DocumentType DocumentType,
    bool IsActive)
    : IRequest<UpdatePrintingTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}

public sealed record UpdatePrintingTemplateResult(Guid Id, string Name, DocumentType DocumentType, bool IsDefault, bool IsActive);
