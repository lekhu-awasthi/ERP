using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreatePrintingTemplate;

public sealed record CreatePrintingTemplateCommand(Guid OrganizationId, string Name, DocumentType DocumentType)
    : IRequest<CreatePrintingTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}

public sealed record CreatePrintingTemplateResult(Guid Id, string Name, DocumentType DocumentType, bool IsDefault);
