using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GetBankReconciliation;

public sealed class GetBankReconciliationQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBankReconciliationQuery, BankReconciliationDetailDto>
{
    public async Task<BankReconciliationDetailDto> Handle(
        GetBankReconciliationQuery request, CancellationToken cancellationToken)
    {
        var reconciliation = await db.BankReconciliations.FirstOrDefaultAsync(
            x => x.Id == request.ReconciliationId
                 && x.OrganizationId == request.OrganizationId
                 && x.BankAccountId == request.BankAccountId,
            cancellationToken);

        if (reconciliation is null)
        {
            throw new NotFoundException("Reconciliation not found.");
        }

        var statementLines = await db.BankStatementLines
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.ReconciliationId == request.ReconciliationId)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var reader = new BankBookTransactionReader(db);

        var movements = await reader.ForReconciliationAsync(
            request.OrganizationId, request.BankAccountId, request.ReconciliationId, cancellationToken);

        var bookTransactions = await reader.DescribeAsync(
            request.OrganizationId, request.BankAccountId, movements, cancellationToken);

        var reconciledByName = await db.Users
            .Where(x => x.Id == reconciliation.ReconciledByUserId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(cancellationToken);

        // Computed from the rows this reconciliation actually holds, which is the only figure that
        // cannot be wrong. Both sides are equal by the aggregate's invariant, so reading either
        // gives the same answer -- the bank side is read because it is the one the user started from.
        var amount = BankMovementTotal.OfStatementLines(statementLines.Select(x => x.Amount));

        return new BankReconciliationDetailDto(
            reconciliation.Id,
            reconciliation.BankAccountId,
            reconciliation.ReconciledAt,
            reconciliation.ReconciledByUserId,
            reconciledByName,
            amount.Signed,
            [.. statementLines.Select(x => new BankStatementLineListItem(
                x.Id,
                x.Date,
                x.Description,
                x.Amount.DepositAmount,
                x.Amount.WithdrawalAmount,
                x.Amount.Signed,
                x.ImportJobId,
                x.ReconciliationId,
                x.CreatedAt))],
            bookTransactions);
    }
}
