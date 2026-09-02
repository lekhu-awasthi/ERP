using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.DeleteProductVariant;

/// <summary>
/// Hard-deletes a variant, and only while nothing has ever transacted against it. A variant that
/// has stock layers, kardex movements, an opening-stock line or any document line is refused with
/// a 409 and must be deactivated instead -- the same rule the rest of this codebase applies to
/// master data, made explicit here because a variant is far likelier to be created by mistake than
/// an ordinary product is (one wrong click in a generated matrix).
/// </summary>
public sealed record DeleteProductVariantCommand(Guid OrganizationId, Guid VariantId)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}
