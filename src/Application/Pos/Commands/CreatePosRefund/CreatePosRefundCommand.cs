using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.CreatePosRefund;

/// <summary>One returned line: which line of the sale, and how much of it came back, in the unit the
/// line was sold in.</summary>
public sealed record PosRefundLineInput(Guid InvoiceLineId, decimal Quantity);

/// <summary>
/// Phase 63 -- a refund at the till: a Credit Note against a till sale, created <b>and approved</b> in
/// one command, with what is handed back posted as a second entry, in the caller's open session at
/// <see cref="LocationId"/>.
///
/// <para><b>Named Create, not Refund</b>, because <c>AuditBehavior</c> audits verbs and a request
/// named <c>RefundPosSale</c> would carry <see cref="IAuditableRequest"/> and write nothing (phase 61's
/// gotcha).</para>
///
/// <para><b>Permissions (phase-63-status.md Decision E).</b> The pipeline checks
/// <c>Sales.CreditNote.Create</c> at the till's location. The handler <b>always</b> re-checks
/// <c>Sales.CreditNote.Approve</c> there too: the note is created approved, so it is exactly what an
/// ERP credit note needs both keys for, and a refund is money leaving the drawer -- the review a sale
/// paid in full does not need. A default Member holds Create and not Approve, so a Member cashier can
/// sell but not refund until an Admin grants Approve at that branch.</para>
/// </summary>
public sealed record CreatePosRefundCommand(
    Guid OrganizationId,
    Guid SessionId,
    Guid? LocationId,
    Guid InvoiceId,
    IReadOnlyList<PosRefundLineInput> Lines,
    IReadOnlyList<PosTenderInput> Payouts,
    string Reason)
    : IRequest<CreatePosRefundResult>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILockDateSensitive,
        IAuditableRequest, IMeteredTransaction, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.CreditNoteCreate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    /// <summary>The Nepal date the refund is dated and posted on. Not bindable from a request body, for
    /// the sale's reason: a till acts now.</summary>
    public DateOnly Date { get; init; } = NepalTime.LocalDate(DateTimeOffset.UtcNow);

    public DocumentType AuditDocumentType => DocumentType.CreditNote;

    /// <summary>An approved Credit Note, so it spends the transaction allowance an ERP approve does.</summary>
    public DocumentType MeteredDocumentType => DocumentType.CreditNote;
}

/// <param name="PaidOut">What was handed back over the counter.</param>
/// <param name="ToAccount">What came off what the customer still owed on the sale instead.</param>
public sealed record CreatePosRefundResult(
    Guid Id,
    string Code,
    decimal GrandTotal,
    decimal ServiceCharge,
    decimal RoundOff,
    decimal PaidOut,
    decimal ToAccount);
