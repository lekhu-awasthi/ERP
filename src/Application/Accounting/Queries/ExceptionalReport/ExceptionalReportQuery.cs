using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ExceptionalReport;

/// <summary>
/// The Analytics Report group's <b>Exceptional Report</b> (phase 26c, slug
/// <c>exceptional-report</c>) -- a fixed-row anomaly sweep. Generated live on 2026-09-03: filter
/// Period only; two columns, Particulars and Balance; twelve named rows in a fixed order, each a
/// magnitude with a DR/CR marker -- <b>except the two inventory rows, which carry no marker at
/// all</b>, because a quantity and a stock valuation do not sit on a side of the ledger. That
/// detail is honoured rather than smoothed over.
///
/// <para><b>Twelve rows, three queries -- one parameterised sweep, not twelve reports.</b> Every
/// account row is a predicate over one pass of GL balances joined to the chart of accounts; both
/// contact rows are one pass of <c>ContactLedgerReader</c> per side; both inventory rows are one
/// pass of <c>StockFactReader</c>. Twelve independent queries would multiply the round trips by
/// four and, worse, let two rows that read the same accounts disagree.</para>
///
/// <para><b>Balances are as of ToDate</b>, like every other balance in this codebase, even though
/// the filter is styled as a period -- "Expense accounts with credit balances" is a question about
/// a position, not about a window.</para>
/// </summary>
public sealed record ExceptionalReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate)
    : IRequest<ExceptionalReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExceptionalReportView;
}

/// <summary>
/// <paramref name="BalanceType"/> is "DR"/"CR" for the ten ledger rows and <b>null</b> for the two
/// inventory rows, matching the live report exactly.
/// <paramref name="IsModelled"/> is false for the one row this codebase has no concept behind; the
/// screen and the .xlsx say so beside the zero rather than presenting it as a real finding.
/// </summary>
public sealed record ExceptionalReportRowDto(
    string Particulars,
    decimal Balance,
    string? BalanceType,
    bool IsModelled = true);

public sealed record ExceptionalReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ExceptionalReportRowDto> Rows);
