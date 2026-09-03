using ErpApp.Domain.Accounting;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.IncomeStatement;

/// <summary>
/// Income minus Expense accounts, GlLines where GlJournalEntry.PostedAt falls in
/// [FromDate, ToDate] (end of day UTC on ToDate, start of day UTC on FromDate -- see
/// GlDateBoundary). Same PostedAt-not-business-Date approximation as TrialBalanceQuery/
/// BalanceSheetQuery -- see phase-8a-status.md.
///
/// <para><b>Compare (Phase 26a, FR-9.1).</b> When Compare is set the handler runs a second
/// aggregation over <see cref="Reports.ComparePeriod.SameLengthPrior"/> -- the same-length window
/// ending the day before FromDate -- and returns it as an extra column per row. Note what this
/// does to the <i>row set</i>: this report lists only accounts with movement in the period, so
/// with Compare on the rows become the <b>union</b> of accounts with movement in either window.
/// An account that traded last period and not this one has to appear, or the comparison silently
/// hides exactly the change the reader opened the report to see. Off by default; when off every
/// Compare* field is null rather than zero, and the row set is unchanged from Phase 8a's.</para>
/// </summary>
public sealed record IncomeStatementQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate, bool Compare = false)
    : IRequest<IncomeStatementDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.IncomeStatementView;
}

public sealed record IncomeStatementRowDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountRootType RootType,
    decimal Amount,
    decimal? CompareAmount = null);

public sealed record IncomeStatementDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<IncomeStatementRowDto> IncomeRows,
    IReadOnlyList<IncomeStatementRowDto> ExpenseRows,
    decimal TotalIncome,
    decimal TotalExpense,
    DateOnly? CompareFromDate = null,
    DateOnly? CompareToDate = null,
    decimal? CompareTotalIncome = null,
    decimal? CompareTotalExpense = null)
{
    public decimal NetIncome => TotalIncome - TotalExpense;

    public decimal? CompareNetIncome =>
        CompareTotalIncome is { } income && CompareTotalExpense is { } expense ? income - expense : null;
}
