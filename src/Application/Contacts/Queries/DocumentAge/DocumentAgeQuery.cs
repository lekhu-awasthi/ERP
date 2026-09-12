using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.Ageing;
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
/// across a year boundary. A document with no due date of its own (a Journal Voucher, an opening
/// balance) ages from its own date, which is what the live screen shows: those rows print Due Date
/// equal to Date. <c>ContactAgeingSummaryQuery</c> once differed here, and since phase 36 cannot:
/// both read <c>OutstandingDocumentReader</c>, so this report's rows are exactly the rows that
/// report puts in buckets.</para>
///
/// <para><b><see cref="AsOfDate"/> is the only date that filters.</b> The live report's period
/// picker has a From end, and it does nothing -- rows dated more than a year before the stated
/// period start came back in the same run. That is correct for an ageing report and matches
/// <c>ContactAgeingSummaryQuery</c>'s single as-of date; <see cref="FromDate"/> is carried purely
/// so the screen and the <c>.xlsx</c> can echo the period the user typed.</para>
///
/// <para><b>Which document types are ageable</b> is enumerated by <see cref="AgeableDocumentType"/>
/// rather than by <c>DocumentType</c>, because the two sets are not the same thing -- see that
/// enum's own comment for the live option this codebase cannot express. Phase 36 moved it beside
/// <c>OutstandingDocumentReader</c>, which is now the one place both ageing reports get their
/// outstanding figures from.</para>
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
    bool ExportAll = false,
    // Phase 35b -- the Billing Location filter; null is "All locations". See ILocationFilteredReport.
    Guid? LocationId = null)
    : IRequest<DocumentAgeDto>, IRequirePermission, IOrganizationScoped, ILocationFilteredReport
{
    public string PermissionKey =>
        ContactType == ContactType.Customer ? PermissionKeys.InvoiceAgeView : PermissionKeys.PurchaseBillAgeView;
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
