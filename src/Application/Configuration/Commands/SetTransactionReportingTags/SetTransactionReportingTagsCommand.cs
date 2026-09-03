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

/// <summary>
/// Applicability comes from <see cref="DocumentMechanisms.ReportingTags"/> -- all 15 transactional
/// types plus OpeningBalance and OpeningStock, the widest of the four sweeps. The key comes from
/// <see cref="DocumentPermissions"/>, shared with every other document-attached mechanism.
/// </summary>
public static class TransactionReportingTagPermissions
{
    public static string EditPermissionFor(DocumentType documentType) =>
        DocumentPermissions.EditPermissionFor(Applicable(documentType));

    public static string ViewPermissionFor(DocumentType documentType) =>
        DocumentPermissions.ViewPermissionFor(Applicable(documentType));

    private static DocumentType Applicable(DocumentType documentType) =>
        DocumentMechanisms.ReportingTags.Contains(documentType)
            ? documentType
            : throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType, "Reporting tags do not apply to this document type.");
}
