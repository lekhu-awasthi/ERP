using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.SetCustomStatus;

/// <summary>
/// Assigns (or clears, when <see cref="CustomStatusId"/> is null) a tenant-defined CustomStatus
/// pipeline value to a document -- Phase 20b, the write-side half of Phase 2's CustomStatus lookup
/// (docs/phase-20b-status.md). Unlike SetCustomFieldValuesCommand (inline in the document's own
/// form) and SetTransactionReportingTagsCommand (a sidebar "Add/Edit" action on the detail page),
/// this is a THIRD shape: live-confirmed against the real Tigg tenant to live only in the
/// Quotation/Purchase Order LIST grid (a "Stage" column per row), applying instantly on selection
/// with no document Save action and no presence on the detail page at all. Orthogonal to the
/// document's own Draft/Approved/Void/Converted lifecycle -- confirmed live, settable on both
/// Draft and Approved rows, no interaction with Approve/Void/Convert, no GL/stock side effect.
/// Deliberately not ILockDateSensitive/ILockDateSensitiveDocument -- it carries no business Date
/// and no GL/financial weight, the same reasoning that kept Phase 20a's Custom Fields unlocked.
/// Rides on the target document's own Edit permission (CustomStatusPermissions), same reasoning
/// as CustomFieldValuePermissions/TransactionReportingTagPermissions -- no new PermissionKeys.
/// </summary>
public sealed record SetCustomStatusCommand(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId, Guid? CustomStatusId)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => CustomStatusPermissions.EditPermissionFor(DocumentType);
}

/// <summary>
/// Applicability comes from <see cref="DocumentMechanisms.CustomStatus"/> -- Quotation, Sales Order,
/// Purchase Order and Production Order, the four of the five live Custom Status sections whose
/// pipeline is genuinely orthogonal to the native lifecycle. Cheque remains excluded rather than
/// deferred (phase-20b's finding: its five custom values are the five members of ChequeStatus).
/// The key comes from <see cref="DocumentPermissions"/>.
/// </summary>
public static class CustomStatusPermissions
{
    public static string EditPermissionFor(DocumentType documentType) =>
        DocumentMechanisms.CustomStatus.Contains(documentType)
            ? DocumentPermissions.EditPermissionFor(documentType)
            : throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType, "Custom status does not apply to this document type.");
}
