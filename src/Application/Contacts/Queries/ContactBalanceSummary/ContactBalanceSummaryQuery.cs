using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ContactBalanceSummary;

/// <summary>
/// Customer Receivable Summary and Supplier Payable Summary -- one shared handler discriminated by
/// <see cref="ContactType"/>, the same way <c>ContactAgeingSummaryQuery</c> and
/// <c>ContactStatementQuery</c> already answer their two report screens each (phase-9's
/// "near-zero-new-code" precedent, itself borrowed from <c>Payment.Direction</c>).
///
/// <para><b>Both shapes were read live</b> on the Moonbeam UAT tenant on 2026-09-03 before this DTO
/// existed, and they are an exact mirror pair: filters <i>Period</i> and <i>Contact Group</i>;
/// columns <i>Customer/Supplier</i>, <i>Contact Group</i>, <i>Closing Balance</i>; a footer
/// <b>Total</b> row. That is the third time a Payable screen has turned out to be its Receivable
/// twin (phase-9 found the same for Ageing Summary and Statement), which is why this phase did not
/// fork.</para>
///
/// <para><b>Closing Balance is the same number <c>ContactStatementQuery</c> prints</b>, by
/// construction: both read <c>ContactLedgerReader</c>, so a reader who opens a customer's Statement
/// from this report's row cannot find the two disagreeing. That is the whole reason this report
/// does not compute its own balance from the documents directly.</para>
///
/// <para><b>The period's From date is ignored on purpose.</b> A closing balance is an as-of figure,
/// and every ledger event up to <see cref="ToDate"/> contributes to it regardless of when the
/// period opened -- confirmed live, where a stated period of 17-07-2026 onwards still returned
/// balances carrying documents from 2025. <see cref="FromDate"/> is carried only so the screen and
/// the <c>.xlsx</c> can echo the period the user asked for, exactly as the live subtitle does
/// ("For the period 17-07-2026 to 03-09-2026").</para>
/// </summary>
public sealed record ContactBalanceSummaryQuery(
    Guid OrganizationId,
    ContactType ContactType,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactGroupId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<ContactBalanceSummaryDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        ContactType == ContactType.Customer
            ? PermissionKeys.CustomerReceivableSummaryView
            : PermissionKeys.SupplierPayableSummaryView;
}

/// <summary>
/// <paramref name="ClosingBalance"/> is a signed figure in the report's own direction: positive
/// means the customer owes us (resp. we owe the supplier), negative means the balance leans the
/// other way. <paramref name="BalanceType"/> carries the matching "DR"/"CR" marker from
/// <c>ContactLedgerReader.BalanceType</c>, so a template never has to know which side is normal for
/// which contact type -- the Balance/BalanceType split <c>ContactStatementDto</c> already uses.
///
/// <para>The live product renders a credit balance in parentheses on the Customer screen and with a
/// bare minus on the Supplier one, while both footers use parentheses. That inconsistency is a
/// defect in its formatting rather than a shape worth copying, so this DTO carries one convention
/// and both screens render it identically -- see docs/phase-26b-status.md.</para>
/// </summary>
public sealed record ContactBalanceSummaryRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactGroupName,
    decimal ClosingBalance,
    string BalanceType);

/// <summary><paramref name="TotalClosingBalance"/> is the grand total across every filtered row,
/// not just the current page -- phase-16c's rule, which the live footer Total also obeys.</summary>
public sealed record ContactBalanceSummaryDto(
    ContactType ContactType,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ContactBalanceSummaryRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalClosingBalance,
    string TotalBalanceType);
