using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveInvoice;

/// <summary>OverrideWarning (Phase 7) lets the Angular client resubmit after a
/// StockAvailabilityWarningException without a second round-trip -- architecture-spec.md §3.5's
/// own recommendation for the Warn-and-allow flow. Defaults to false so every existing caller
/// (and every pre-Phase-7 test) keeps behaving exactly as before.
///
/// <para>OverrideCreditLimitWarning (Phase 31) is its twin for the Crossed Credit Limit warning,
/// and is deliberately a <b>second</b> flag rather than a widening of the first: an invoice can trip
/// both warnings, the live product shows them as two dialogs with their own Dismiss/Continue, and
/// one shared flag would let a Continue on the stock dialog silently waive a credit breach the user
/// never saw. See CreditLimitWarningException.</para></summary>
public sealed record ApproveInvoiceCommand(
    Guid OrganizationId, Guid Id, bool OverrideWarning = false, bool OverrideCreditLimitWarning = false)
    : IRequest<ApproveInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InvoiceApprove;
    public DocumentType LockDateDocumentType => DocumentType.Invoice;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveInvoiceResult(Guid Id, string Code, InvoiceStatus Status, DateTimeOffset? ApprovedAt);
