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

/// <summary>Only Quotation and PurchaseOrder are wired up (roadmap Phase 20b scope guard) --
/// live-confirmed to be the only two of the four candidate types (Quotation, SalesOrder,
/// PurchaseOrder, Cheque) this sub-phase builds end-to-end. SalesOrder has the identical shape
/// (aggregate + DocumentType member + Edit key) and is deferred as mechanical follow-up, same as
/// CustomFieldValuePermissions deferred the other 15 document types in Phase 20a. Cheque is
/// deliberately excluded, not deferred -- see phase-20b-status.md's Cheque decision: its
/// "Custom Status" definitions are the exact same 5 values as the native ChequeStatus enum, and
/// the live tenant's Cheque list STATUS column appears to actually drive that lifecycle, not sit
/// orthogonal to it -- wiring it properly is a materially larger task than this sub-phase's scope.
/// Invoice was never a candidate: Configurations > Custom Status has no Invoice section in the
/// live tenant at all, contradicting the kickoff prompt's assumption that it would mirror 20a's
/// Quotation+Invoice duo.</summary>
public static class CustomStatusPermissions
{
    public static string EditPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationEdit,
        DocumentType.PurchaseOrder => PermissionKeys.PurchaseOrderEdit,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Custom status is not wired up for this document type yet."),
    };
}
