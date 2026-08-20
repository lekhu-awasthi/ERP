using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ContactAgeingSummary;

/// <summary>
/// One shared handler answers both Customer and Supplier Ageing Summary (confirmed live shape for
/// both -- Customer per architecture-spec.md line 276/erp-module-scan.md, Supplier read live from
/// the reference product's own Supplier Ageing Summary screen during this phase's design pass,
/// identical columns: Account Name, Contact Group, 1-30/31-60/61-90/91+ Days, Total). ContactType
/// discriminates which underlying document types feed the buckets (Invoice for Customer;
/// PurchaseBill+Expense for Supplier) the same way Payment's own Direction already discriminates
/// Received-vs-Paid in one aggregate (phase-6-status.md's "near-zero-new-code" precedent) rather than
/// forking into two near-identical handlers -- see phase-9-status.md's scope decision on this.
///
/// PermissionKey is computed from ContactType so the two report identities (Customer/Supplier) can
/// still be granted independently, mirroring SalesMasterReportView/PurchaseMasterReportView's
/// precedent even though one handler serves both.
///
/// AsOfDate has no lower bound -- a real ageing report ages *all* historical unpaid bills as of one
/// date, not a period (confirmed live: the Ageing screen's only date control is a single "As Of"
/// picker, no From/To range, unlike Statement).
///
/// The live screen's "Credit Term" column is omitted -- no Contact or document anywhere in this
/// codebase carries a CreditTermId (CreditTerm's own doc comment: "nothing consumes this today"),
/// so there is no real per-Contact due-day figure to source it from. Bucketing is therefore computed
/// from each bill's own Date, not a Date+CreditTerm.DueDays due date -- see phase-9-status.md.
/// </summary>
public sealed record ContactAgeingSummaryQuery(
    Guid OrganizationId,
    ContactType ContactType,
    DateOnly AsOfDate,
    Guid? ContactGroupId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<ContactAgeingSummaryDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        ContactType == ContactType.Customer ? PermissionKeys.CustomerAgeingSummaryView : PermissionKeys.SupplierAgeingSummaryView;
}

public sealed record ContactAgeingSummaryRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactGroupName,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days91Plus)
{
    public decimal Total => Days1To30 + Days31To60 + Days61To90 + Days91Plus;
}

/// <summary>Total* fields are bucket grand totals across every filtered row, not just the current
/// page -- see SalesMasterReportDto's doc comment for why the pre-existing client-side
/// rows().reduce(...) footer total breaks once Rows is a single page.</summary>
public sealed record ContactAgeingSummaryDto(
    DateOnly AsOfDate,
    ContactType ContactType,
    IReadOnlyList<ContactAgeingSummaryRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalDays1To30,
    decimal TotalDays31To60,
    decimal TotalDays61To90,
    decimal TotalDays91Plus);
