using ErpApp.Application.Configuration;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetPrintingTemplateLocationChoice;

/// <summary>
/// Phase 75 -- one location prints documents of the template's type with this template instead of the organization's
/// default. Replaces the location's earlier choice for that type, if any. The type is the template's own, so a request
/// cannot pair a template with a type it does not print.
///
/// <para><b>Permission: <c>Configuration.PrintingTemplate.Manage</c></b>, the key that already sets the organization's
/// default. Choosing a location's template is the same act at a narrower scope, and a configuration key is not one
/// a role grants per location (phase 32b scopes transaction keys only). No new key.</para>
/// </summary>
public sealed record SetPrintingTemplateLocationChoiceCommand(Guid OrganizationId, Guid LocationId, Guid PrintingTemplateId)
    : IRequest<PrintingTemplateLocationChoiceDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}
