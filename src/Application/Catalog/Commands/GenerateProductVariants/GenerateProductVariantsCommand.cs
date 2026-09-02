using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.GenerateProductVariants;

/// <summary>
/// Fills a parent's variant matrix: the cartesian product of the selected options, one variant per
/// combination, skipping combinations that already exist.
///
/// This has no counterpart in the live reference product, which only ever adds variants one at a
/// time -- it exists because FR-8.3 ("variants generated from reusable, tenant-defined attribute
/// definitions") and the roadmap's exit criterion ("a two-attribute product generates its variant
/// matrix") both ask for it explicitly. It shares Product.CreateVariant with the single-add path,
/// so it cannot drift from it.
///
/// **Re-running is safe and is the point.** Skipping rather than failing on an existing
/// combination means "add a fifth colour, generate again" fills only the four new rows -- the
/// idempotency comes from the (OrganizationId, ParentProductId, CombinationKey) unique index, not
/// from the caller being careful.
///
/// Omitting <see cref="Options"/> generates over the product's whole Attributes Used pool.
/// </summary>
public sealed record GenerateProductVariantsCommand(
    Guid OrganizationId,
    Guid ProductId,
    IReadOnlyList<VariantCombinationInput>? Options = null)
    : IRequest<GenerateProductVariantsResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

/// <param name="Created">Variants this run actually created.</param>
/// <param name="SkippedExisting">Combinations that already existed -- reported rather than hidden,
/// so a user regenerating can see the run did something even when Created is small.</param>
public sealed record GenerateProductVariantsResult(
    Guid ProductId, int SkippedExisting, IReadOnlyList<ProductVariantResult> Created);
