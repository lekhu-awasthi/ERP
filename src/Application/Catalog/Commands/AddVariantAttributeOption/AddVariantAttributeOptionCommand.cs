using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.AddVariantAttributeOption;

/// <summary>Backs the "Add New" affordance the live product shows beside each attribute in a
/// product's Attributes Used panel, and the attribute editor's own options list.</summary>
public sealed record AddVariantAttributeOptionCommand(Guid OrganizationId, Guid AttributeId, string Value)
    : IRequest<VariantAttributeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VariantAttributeManage;
}
