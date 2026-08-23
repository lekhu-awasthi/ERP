using ErpApp.Application.Accounting.Queries.BalanceSheet;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.RatioAnalysis;

public sealed class RatioAnalysisQueryHandler(IAppDbContext db, ITreeQuery<AccountGroup> treeQuery)
    : IRequestHandler<RatioAnalysisQuery, RatioAnalysisDto>
{
    public async Task<RatioAnalysisDto> Handle(RatioAnalysisQuery request, CancellationToken cancellationToken)
    {
        var balanceSheet = await new BalanceSheetQueryHandler(db, treeQuery)
            .Handle(new BalanceSheetQuery(request.OrganizationId, request.ToDate), cancellationToken);
        var incomeStatement = await new IncomeStatementQueryHandler(db)
            .Handle(new IncomeStatementQuery(request.OrganizationId, request.FromDate, request.ToDate), cancellationToken);

        var settings = await db.TenantSettings
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken);

        var cutoff = GlDateBoundary.EndOfDayUtc(request.ToDate);
        var receivables = settings?.DefaultAccountsReceivableId is { } arId
            ? await NetDebitBalanceAsync(arId, cutoff, cancellationToken) : 0m;
        var payables = settings?.DefaultAccountsPayableId is { } apId
            ? -await NetDebitBalanceAsync(apId, cutoff, cancellationToken) : 0m;

        // Not the GL DefaultInventoryAccountId balance -- PurchaseBillPostingRule debits a Purchase
        // (Expense) account, not Inventory, so that GL account only ever receives Invoice's own COGS-
        // relief credit and runs permanently negative, never reflecting real stock value (confirmed
        // via Phase 19 manual E2E, a Trial Balance check against a fresh seeded org). Same
        // FIFO-layer valuation Stock Ageing/Product Profitability already use instead.
        var inventory = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && x.TransactionDate <= request.ToDate)
            .SumAsync(x => x.QuantityRemaining * x.UnitCost, cancellationToken);

        var bankAccountIds = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId && (a.Kind == AccountKind.Bank || a.Kind == AccountKind.Cash))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        var cashAndBank = 0m;
        foreach (var accountId in bankAccountIds)
        {
            cashAndBank += await NetDebitBalanceAsync(accountId, cutoff, cancellationToken);
        }

        var invoiceIds = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var salesLines = await db.InvoiceLines
            .Where(x => invoiceIds.Contains(x.InvoiceId))
            .Select(x => new { x.Amount, x.Quantity, x.CogsUnitCost })
            .ToListAsync(cancellationToken);
        var sales = salesLines.Sum(x => x.Amount);
        var costOfSales = salesLines.Sum(x => (x.CogsUnitCost ?? 0) * x.Quantity);

        var purchaseBillIds = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var purchases = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
            .SumAsync(x => x.Amount, cancellationToken);

        var days = Math.Max(1, request.ToDate.DayNumber - request.FromDate.DayNumber + 1);
        var netProfit = incomeStatement.TotalIncome - incomeStatement.TotalExpense;

        var currentRatio = SafeDivide(receivables + inventory + cashAndBank, payables);
        var quickRatio = SafeDivide(receivables + cashAndBank, payables);
        var cashRatio = SafeDivide(cashAndBank, payables);
        var debtToEquity = SafeDivide(balanceSheet.TotalLiabilities, balanceSheet.TotalEquity);
        var debtRatio = SafeDivide(balanceSheet.TotalLiabilities, balanceSheet.TotalAssets);
        var inventoryTurnover = SafeDivide(costOfSales, inventory);
        var receivablesTurnover = SafeDivide(sales, receivables);
        var assetTurnover = SafeDivide(sales, balanceSheet.TotalAssets);
        var receivableDays = SafeDivide(receivables, sales) * days;
        var payableDays = SafeDivide(payables, purchases) * days;
        var inventoryHoldingDays = SafeDivide(inventory, costOfSales) * days;
        var cashConversionCycle = inventoryHoldingDays + receivableDays - payableDays;
        var grossProfitMarginPct = SafeDivide(sales - costOfSales, sales) * 100m;
        var netProfitMarginPct = SafeDivide(netProfit, sales) * 100m;
        var returnOnAssetsPct = SafeDivide(netProfit, balanceSheet.TotalAssets) * 100m;
        var returnOnEquityPct = SafeDivide(netProfit, balanceSheet.TotalEquity) * 100m;

        return new RatioAnalysisDto(
            request.FromDate, request.ToDate,
            currentRatio, quickRatio, cashRatio,
            debtToEquity, debtRatio,
            inventoryTurnover, receivablesTurnover, assetTurnover, receivableDays, payableDays,
            inventoryHoldingDays, cashConversionCycle,
            grossProfitMarginPct, netProfitMarginPct, returnOnAssetsPct, returnOnEquityPct);
    }

    private async Task<decimal> NetDebitBalanceAsync(Guid accountId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var totals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where line.AccountId == accountId && entry.PostedAt <= cutoff
            select new { line.Debit, line.Credit })
            .ToListAsync(cancellationToken);

        return totals.Sum(x => x.Debit) - totals.Sum(x => x.Credit);
    }

    private static decimal SafeDivide(decimal numerator, decimal denominator) => denominator == 0 ? 0 : numerator / denominator;
}
