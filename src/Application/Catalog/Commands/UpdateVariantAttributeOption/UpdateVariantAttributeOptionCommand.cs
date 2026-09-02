using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttributeOption;

/// <summary>
/// Decision C's answer to "what happens when a user removes an option variants already exist for?"
/// -- split in two, because the two halves have genuinely different answers:
///
/// **Rename is always allowed.** The option's identity is its Id, not its text, so every existing
/// variant keeps pointing at the same row and simply displays the corrected label. That is what a
/// user fixing a typo actually wants, and it strands nothing.
///
/// **Deactivation is always allowed too, and is purely forward-looking**: it retires the option
/// from future variant creation while every existing variant, its stock and its history stay
/// intact and readable. An option is never hard-deleted -- see VariantAttributeOption's doc
/// comment.
///
/// The refusal lives one level up instead, in SetProductVariantAttributesCommandHandler: dropping
/// an option from a *product's* Attributes Used pool while one of that product's variants is built
/// from it would leave the child built from something its parent no longer offers, so that is a
/// 409. Retiring the catalog option is safe; un-offering it on a product in use is not.
/// </summary>
public sealed record UpdateVariantAttributeOptionCommand(
    Guid OrganizationId, Guid AttributeId, Guid OptionId, string Value, bool IsActive)
    : IRequest<VariantAttributeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VariantAttributeManage;
}
