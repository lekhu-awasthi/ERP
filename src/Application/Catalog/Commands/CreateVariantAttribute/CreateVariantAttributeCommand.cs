using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateVariantAttribute;

/// <summary>Confirmed live: the create form is Name* plus a repeating options list, nothing more --
/// options are submitted with the attribute, not added afterwards.</summary>
public sealed record CreateVariantAttributeCommand(Guid OrganizationId, string Name, IReadOnlyList<string> Options)
    : IRequest<VariantAttributeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VariantAttributeManage;
}

public sealed record VariantAttributeResult(Guid Id, string Name, bool IsActive, IReadOnlyList<VariantAttributeOptionResult> Options);

public sealed record VariantAttributeOptionResult(Guid Id, string Value, int SortOrder, bool IsActive);
