using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.BankReconciliationReport;

public sealed class BankReconciliationReportQueryHandler(IAppDbContext db)
    : IRequestHandler<BankReconciliationReportQuery, BankReconciliationReportDto>
{
    public async Task<BankReconciliationReportDto> Handle(
        BankReconciliationReportQuery request, CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .Where(x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Code, x.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            throw new NotFoundException("Bank account not found.");
        }

        // Nepal's wall clock, not the server's: "as of today" for a tenant means the day it is in
        // Kathmandu (phase 20e's rule, and the reason NepalTime exists).
        var asOf = request.AsOfDate ?? NepalTime.LocalDate(DateTimeOffset.UtcNow);

        var reader = new BankBookTransactionReader(db);

        var allBook = new BookMovementFilter(
            request.OrganizationId, request.BankAccountId, Reconciled: null, ToDate: asOf);

        var unreconciledBook = allBook with { Reconciled = false };

        var bookBalance = await reader.SumAsync(allBook, cancellationToken);

        var unreconciledBookCount = await reader.CountAsync(unreconciledBook, cancellationToken);

        // A count of zero is a complete answer -- no sum and no page query after one (phase 42).
        var unreconciledBookTotal = unreconciledBookCount == 0
            ? BankMovementTotal.Zero
            : await reader.SumAsync(unreconciledBook, cancellationToken);

        var bookRows = unreconciledBookCount == 0
            ? []
            : await reader.DescribeAsync(
                request.OrganizationId,
                request.BankAccountId,
                await reader.PageAsync(
                    unreconciledBook, request.UnrecognizedPage, request.UnrecognizedPageSize, cancellationToken),
                cancellationToken);

        // The bank side cuts off on the statement line's own Date -- see the query for why the two
        // sides deliberately use different date fields.
        var bankLines = db.BankStatementLines.Where(
            x => x.OrganizationId == request.OrganizationId
                 && x.BankAccountId == request.BankAccountId
                 && x.Date <= asOf);

        // The bank side is summed in memory, and unlike the book side it has to be: StatementAmount
        // reaches the store through a value converter, so `x.Amount.Signed` is a property on a CLR
        // struct the provider knows only as a decimal column and cannot be translated -- the family
        // of failures where InMemory evaluates in C# and SQL Server 500s (phase 42, and this phase's
        // own bug). Only the column itself comes back, one decimal per row.
        //
        // The cost is honest rather than hidden: this is linear in the account's statement history
        // up to the as-of date, which is what a balance is over. RE-ENTRY CONDITION: a tenant whose
        // statement history is large enough for this report to be slow, at which point the fix is a
        // store-side sum -- and the only way to get one is a second, shadowed decimal column, which
        // phase 55 ruled against for good reasons that would have to be weighed against a measured
        // number rather than against this comment.
        var bankBalance = BankMovementTotal.OfStatementLines(
            await bankLines.Select(x => x.Amount).ToListAsync(cancellationToken));

        var unreconciledBankQuery = bankLines.Where(x => x.ReconciliationId == null);

        var unreconciledBankCount = await unreconciledBankQuery.CountAsync(cancellationToken);

        var unreconciledBankAmounts = unreconciledBankCount == 0
            ? []
            : await unreconciledBankQuery.Select(x => x.Amount).ToListAsync(cancellationToken);

        var unreconciledBankTotal = BankMovementTotal.OfStatementLines(unreconciledBankAmounts);

        var bankRows = unreconciledBankCount == 0
            ? new List<BankStatementLine>()
            : await unreconciledBankQuery
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Id)
                .Skip((request.UnrecognizedPage - 1) * request.UnrecognizedPageSize)
                .Take(request.UnrecognizedPageSize)
                .ToListAsync(cancellationToken);

        return new BankReconciliationReportDto(
            request.BankAccountId,
            account.Code,
            account.Name,
            asOf,
            bookBalance.Signed,
            bankBalance.Signed,
            (bankBalance - bookBalance).Signed,
            unreconciledBookTotal.Signed,
            unreconciledBookCount,
            unreconciledBankTotal.Signed,
            unreconciledBankCount,
            bookRows,
            [.. bankRows.Select(x => new BankStatementLineListItem(
                x.Id,
                x.Date,
                x.Description,
                x.Amount.DepositAmount,
                x.Amount.WithdrawalAmount,
                x.Amount.Signed,
                x.ImportJobId,
                x.ReconciliationId,
                x.CreatedAt))]);
    }
}
