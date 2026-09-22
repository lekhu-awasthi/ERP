using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.CreateBankReconciliation;

public sealed class CreateBankReconciliationCommandHandler(
    IAppDbContext db,
    ICurrentUserService currentUser)
    : IRequestHandler<CreateBankReconciliationCommand, CreateBankReconciliationResult>
{
    public async Task<CreateBankReconciliationResult> Handle(
        CreateBankReconciliationCommand request, CancellationToken cancellationToken)
    {
        var accountExists = await db.Accounts.AnyAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken);

        if (!accountExists)
        {
            throw new NotFoundException("Bank account not found.");
        }

        var statementLineIds = request.StatementLineIds;
        var glLineIds = request.GlLineIds;

        var statementLines = await db.BankStatementLines
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.BankAccountId == request.BankAccountId
                        && statementLineIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        // Every id the caller named has to resolve. A partial match would reconcile a smaller set
        // than the user ticked -- and, because the sum rule would then be applied to that smaller
        // set, it would usually fail with a confusing total rather than saying what was wrong.
        if (statementLines.Count != statementLineIds.Distinct().Count())
        {
            throw new NotFoundException(
                "One or more bank statement lines were not found on this account.");
        }

        // The book side has to be reached through its entry, because that is where OrganizationId
        // lives -- GlLine is a child row and carries no tenant column of its own. AccountId on the
        // line is what restricts this to movements of *this* bank account's money, which is the
        // precondition BankMovementTotal.OfGlLines documents and does not enforce for itself.
        var bookLines = await db.GlLines
            .Where(x => x.AccountId == request.BankAccountId && glLineIds.Contains(x.Id))
            .Join(
                db.GlJournalEntries.Where(e => e.OrganizationId == request.OrganizationId),
                line => line.GlJournalEntryId,
                entry => entry.Id,
                (line, _) => line)
            .ToListAsync(cancellationToken);

        if (bookLines.Count != glLineIds.Distinct().Count())
        {
            throw new NotFoundException(
                "One or more book transactions were not found on this account.");
        }

        // The already-reconciled refusal, the sum rule and the mutation of both sides all live in
        // BankReconciliationWriter, because phase 57 added a second way to reach them (Quick
        // Approve) and two copies of a rule agree only by coincidence (phase 36).
        var write = BankReconciliationWriter.Reconcile(
            request.OrganizationId,
            request.BankAccountId,
            statementLines,
            bookLines,
            currentUser.UserId,
            DateTimeOffset.UtcNow,
            nameof(request.StatementLineIds));

        db.BankReconciliations.Add(write.Reconciliation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBankReconciliationResult(
            write.Reconciliation.Id,
            write.Total.Signed,
            statementLines.Count,
            bookLines.Count);
    }
}
