using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;

/// <summary>
/// Replaces the full set of ReportingTagOptions attached to one document -- matching the live
/// reference product's "Add/Edit" dialog (Phase 19 decision #1), which edits the whole tag set for
/// a document at once rather than exposing separate attach/detach actions. Rides on that document
/// type's own Edit permission (see PermissionKey below) rather than a new key -- tagging is a
/// detail-page edit action, not a distinct capability (decision #7's reasoning).
/// </summary>
public sealed record SetTransactionReportingTagsCommand(
    Guid OrganizationId, DocumentType DocumentType, Guid DocumentId, IReadOnlyList<Guid> TagOptionIds)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => TransactionReportingTagPermissions.EditPermissionFor(DocumentType);
}

/// <summary>Only the document types live-confirmed to carry a Reporting Tags control (decision #1)
/// are supported -- Quotation (confirmed directly) and Invoice (confirmed via the scan's field list
/// and the live "This is export sales" create-form pass). Any other DocumentType is a validation
/// error, not silently accepted.</summary>
public static class TransactionReportingTagPermissions
{
    public static string EditPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationEdit,
        DocumentType.Invoice => PermissionKeys.InvoiceEdit,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Reporting tags are not supported on this document type."),
    };

    public static string ViewPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.Invoice => PermissionKeys.InvoiceView,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Reporting tags are not supported on this document type."),
    };
}
