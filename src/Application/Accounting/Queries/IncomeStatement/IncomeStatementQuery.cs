using ErpApp.Domain.Accounting;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.IncomeStatement;

/// <summary>
/// Income minus Expense accounts, GlLines where GlJournalEntry.PostedAt falls in
/// [FromDate, ToDate] (end of day UTC on ToDate, start of day UTC on FromDate -- see
/// GlDateBoundary). Same PostedAt-not-business-Date approximation as TrialBalanceQuery/
/// BalanceSheetQuery -- see phase-8a-status.md.
/// </summary>
public sealed record IncomeStatementQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<IncomeStatementDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.IncomeStatementView;
}

public sealed record IncomeStatementRowDto(Guid AccountId, string AccountCode, string AccountName, AccountRootType RootType, decimal Amount);

public sealed record IncomeStatementDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<IncomeStatementRowDto> IncomeRows,
    IReadOnlyList<IncomeStatementRowDto> ExpenseRows,
    decimal TotalIncome,
    decimal TotalExpense)
{
    public decimal NetIncome => TotalIncome - TotalExpense;
}
