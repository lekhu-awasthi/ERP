using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.BankBalanceHistory;

/// <summary>
/// Phase 57 — the <b>Balance History</b> chart: the two balances, per day, for a window ending on
/// the as-of date. The reference product's <c>GET /balance-history/:id</c>, which returns
/// <c>[{day, tigg_balance, bank_balance}]</c> for the last 30 days and draws two lines on the
/// account Overview.
///
/// <para><b>No new source of truth.</b> Both series are cumulative balances of the same two records
/// the Reconciliation Report already shows — this tenant's GL movements through the account, and the
/// imported statement lines — so the last point of each series equals that report's own
/// <c>BookBalance</c> and <c>BankBalance</c> on the same as-of date. That is by construction rather
/// than by coincidence (phase 26b's shared-reader rule, restated in 36) and
/// <c>BankBalanceHistoryAgreementTests</c> is the test that reads both on the same data.</para>
///
/// <para><b>Which day, and a deliberate divergence from this phase's own brief.</b> The plan for
/// this phase said a day here should be a Nepal day. It is not: the book side's day is the
/// <b>UTC</b> day of <c>PostedAt</c>, because that is what <c>GlDateBoundary</c> cuts every GL report
/// in this codebase on and what <c>BankBookTransactionReader</c> already projects. Anchoring the
/// chart to Kathmandu while the report it must agree with is anchored to UTC would put up to one
/// day's movements on the wrong side of the line, and a chart that disagrees with the figure printed
/// above it is worse than a chart on the wrong calendar. Only "what is today" uses
/// <c>NepalTime</c> — as the report already does — and every label is rendered through
/// <c>NepaliDatePipe</c> like every other date the app outputs (phase 48). The bank side has no such
/// question: a statement line carries a plain <c>DateOnly</c>, the bank's own value date.</para>
///
/// <para>It rides <see cref="PermissionKeys.BankStatementView"/> with the Reconciliation Report, for
/// the reasons set out there — it is the same two figures, plotted.</para>
/// </summary>
/// <param name="AsOfDate">The last day on the chart. Null means today in Kathmandu, which is what
/// the screen opens on.</param>
/// <param name="Days">How many days the window covers, the last one being
/// <paramref name="AsOfDate"/>. Defaults to the reference product's 30.</param>
public sealed record BankBalanceHistoryQuery(
    Guid OrganizationId,
    Guid BankAccountId,
    DateOnly? AsOfDate = null,
    int Days = BankBalanceHistoryQuery.DefaultDays)
    : IRequest<BankBalanceHistoryDto>, IRequirePermission, IOrganizationScoped
{
    public const int DefaultDays = 30;

    /// <summary>A year. Not a paging limit — the series is one row per day and the cost is in the
    /// two opening sums, which are the same whatever the window — but a bound, so a caller cannot
    /// ask for a series with more points than a chart can draw.</summary>
    public const int MaxDays = 366;

    public string PermissionKey => PermissionKeys.BankStatementView;
}

/// <param name="BookBalance">"Balance In TIGG App" as at the end of <paramref name="Day"/>.</param>
/// <param name="BankBalance">The imported statement's balance as at the end of the same day.</param>
/// <param name="Difference">Bank less book, the figure the report highlights in red.</param>
public sealed record BankBalanceHistoryPoint(
    DateOnly Day,
    decimal BookBalance,
    decimal BankBalance,
    decimal Difference);

/// <param name="Points">One per day of the window, oldest first, <b>including days on which nothing
/// moved</b> — a balance chart with gaps for quiet days would misread as a balance that changed on
/// the days either side of the gap.</param>
public sealed record BankBalanceHistoryDto(
    Guid BankAccountId,
    string AccountCode,
    string AccountName,
    DateOnly FromDate,
    DateOnly AsOfDate,
    IReadOnlyList<BankBalanceHistoryPoint> Points);
