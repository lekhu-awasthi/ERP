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

/// <summary>Only the document types wired up so far (Quotation, Invoice -- Phase 20a's scope guard)
/// are supported. CustomFieldDefinition itself applies to all 17 document types (confirmed live in
/// the "+ADD NEW FIELD" checkboxes), but rolling every type's UI out is explicitly deferred as
/// mechanical follow-up work, same split as TransactionReportingTagPermissions.</summary>
public static class CustomFieldValuePermissions
{
    public static string EditPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationEdit,
        DocumentType.Invoice => PermissionKeys.InvoiceEdit,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Custom field values are not wired up for this document type yet."),
    };

    public static string ViewPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.Invoice => PermissionKeys.InvoiceView,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Custom field values are not wired up for this document type yet."),
    };
}
