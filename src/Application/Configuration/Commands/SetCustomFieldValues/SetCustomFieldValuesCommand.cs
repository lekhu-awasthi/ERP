using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetCustomFieldValues;

/// <summary>
/// Replaces the full set of CustomFieldValues attached to one document -- same replace-the-whole-set
/// shape as SetTransactionReportingTagsCommand, but the UX it backs is different: live-confirmed
/// against the real Tigg "Add New Invoice" form (Phase 20a), Custom Fields render inline in the
/// document's own create/edit form (a "Custom Fields" section above the totals), not behind a
/// separate "Add/Edit" action the way Reporting Tags are. That's a frontend orchestration detail
/// only -- the Angular editor calls this command right after the parent document's own Create/Update
/// succeeds (so DocumentId exists), under the same single "Save" click from the user's perspective.
/// Rides on that document type's own Edit permission, not a new key -- same reasoning as reporting
/// tags (decision recorded in phase-20a-status.md).
/// </summary>
public sealed record SetCustomFieldValuesCommand(
    Guid OrganizationId, DocumentType DocumentType, Guid DocumentId, IReadOnlyList<CustomFieldValueInput> Values)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => CustomFieldValuePermissions.EditPermissionFor(DocumentType);
}

public sealed record CustomFieldValueInput(Guid FieldDefinitionId, string Value);

/// <summary>
/// Applicability comes from <see cref="DocumentMechanisms.CustomFields"/> -- 13 document types,
/// live-confirmed as the 16 sections Configurations &gt; Custom Fields renders (the four live
/// payment kinds collapse onto this codebase's single Payment). Notably <b>not</b> Warehouse
/// Transfer or Inventory Adjustment, which carry Reporting Tags but no Custom Fields section.
/// The key itself comes from <see cref="DocumentPermissions"/>, shared with every other
/// document-attached mechanism.
/// </summary>
public static class CustomFieldValuePermissions
{
    public static string EditPermissionFor(DocumentType documentType) =>
        DocumentPermissions.EditPermissionFor(Applicable(documentType));

    public static string ViewPermissionFor(DocumentType documentType) =>
        DocumentPermissions.ViewPermissionFor(Applicable(documentType));

    private static DocumentType Applicable(DocumentType documentType) =>
        DocumentMechanisms.CustomFields.Contains(documentType)
            ? documentType
            : throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType, "Custom fields do not apply to this document type.");
}
