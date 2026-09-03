using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.DocumentAge;

/// <summary>
/// Invoice Age and Purchase Bill Age -- one shared handler discriminated by
/// <see cref="ContactType"/>, following <c>ContactAgeingSummaryQuery</c>'s precedent. Both screens
/// were read live on 2026-09-03; they carry the same eleven columns, differing only in two header
/// labels ("Invoice Date"/"Invoice Amount" against "Date"/"Amount") and in which document types
/// their Txn Type filter offers.
///
/// <para><b>Age runs from the Due Date, not the document date</b> -- proved live twice over, once
/// across a year boundary. This is the one place this report parts company with
/// <c>ContactAgeingSummaryQuery</c>, which buckets from each bill's own Date because it had no due
/// date to work from; Invoice and PurchaseBill both carry a real <c>DueDate</c>, so this report
/// uses it. A document with no due date of its own (a Journal Voucher, an opening balance) ages
/// from its own date, which is what the live screen shows: those rows print Due Date equal to
/// Date.</para>
///
/// <para><b><see cref="AsOfDate"/> is the only date that filters.</b> The live report's period
/// picker has a From end, and it does nothing -- rows dated more than a year before the stated
/// period start came back in the same run. That is correct for an ageing report and matches
/// <c>ContactAgeingSummaryQuery</c>'s single as-of date; <see cref="FromDate"/> is carried purely
/// so the screen and the <c>.xlsx</c> can echo the period the user typed.</para>
///
/// <para><b>Which document types are ageable</b> is enumerated by <see cref="AgeableDocumentType"/>
/// rather than by <c>DocumentType</c>, because the two sets are not the same thing -- see that
/// enum's own comment for the two live options this codebase cannot express.</para>
/// </summary>
public sealed record DocumentAgeQuery(
    Guid OrganizationId,
    ContactType ContactType,
    DateOnly FromDate,
    DateOnly AsOfDate,
    Guid? ContactId = null,
    IReadOnlyList<AgeableDocumentType>? DocumentTypes = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<DocumentAgeDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        ContactType == ContactType.Customer ? PermissionKeys.InvoiceAgeView : PermissionKeys.PurchaseBillAgeView;
}

/// <summary>
/// The document types that can carry an outstanding balance against a contact, as the live Txn Type
/// filter enumerates them: Opening Balance, the trade document (Invoice on the customer side;
/// Purchase Bill and Expense on the supplier side), and Journal Voucher.
///
/// <para><b>The live filter offers one more option this codebase cannot express</b> -- "Quick
/// Payment" on the receivable side, "Quick Receipt" on the payable side. In the reference product
/// those are generic multi-line Accounts-table documents in their own right; here, phase-17
/// Decision #7 deliberately made Quick Payment/Receipt a thin variant of the existing
/// <c>Payment</c> aggregate rather than a new document type, so there is no such document to age.
/// An unallocated Payment is a credit against the contact, and it already reduces the contact's
/// balance through <c>ContactBalanceSummaryQuery</c>; it is not an outstanding item with an age.
/// Omitted with this note rather than faked.</para>
///
/// <para><b>Opening Balance is a contact's own <c>Contact.OpeningBalance</c></b>, not an
/// <c>OpeningBalanceLine</c>: the latter is keyed by (OrganizationId, AccountId) and carries no
/// contact at all. It has no document number, no reference and no date, so it ages from the
/// as-of date itself -- age zero, status Current -- which is the honest rendering of a figure that
/// records a balance without recording when it arose.</para>
/// </summary>
public enum AgeableDocumentType
{
    OpeningBalance,
    Invoice,
    PurchaseBill,
    Expense,
    JournalVoucher,
}

/// <summary>
/// One outstanding document. <paramref name="Balance"/> is <paramref name="Amount"/> less
/// <paramref name="Paid"/>; only rows with a non-zero balance appear.
/// <paramref name="Status"/> is <c>Overdue</c> once the as-of date has passed the due date and
/// <c>Current</c> otherwise, and <paramref name="AgeDays"/> counts days past due -- zero, never
/// negative, for a document that is not yet due.
/// </summary>
public sealed record DocumentAgeRowDto(
    AgeableDocumentType DocumentType,
    Guid DocumentId,
    DateOnly Date,
    DateOnly DueDate,
    string Number,
    string? ReferenceNo,
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactGroupName,
    decimal Amount,
    decimal Paid,
    decimal Balance,
    string Status,
    int AgeDays)
{
    public const string Overdue = "Overdue";
    public const string Current = "Current";
}

/// <summary>Total* fields span every filtered row, not just the current page (phase-16c).</summary>
public sealed record DocumentAgeDto(
    ContactType ContactType,
    DateOnly FromDate,
    DateOnly AsOfDate,
    IReadOnlyList<DocumentAgeRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalAmount,
    decimal TotalPaid,
    decimal TotalBalance);
