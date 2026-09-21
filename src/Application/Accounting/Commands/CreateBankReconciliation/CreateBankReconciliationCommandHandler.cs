using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
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

        // Already-reconciled rows are a 409 naming the count, not a 404: the row exists and the
        // caller may well be looking at a stale pane after somebody else reconciled it. The
        // aggregate's Reconcile() would throw anyway -- this is here so the failure carries a
        // status code and a sentence rather than surfacing as a 500 (phase 39's rule: a Domain
        // invariant reached through an endpoint is a 500, which tells a caller nothing).
        var alreadyDone =
            statementLines.Count(x => x.ReconciliationId is not null)
            + bookLines.Count(x => x.ReconciliationId is not null);

        if (alreadyDone > 0)
        {
            throw new ConflictException(
                $"{alreadyDone} of the selected transactions are already reconciled. Reload the page "
                + "and select again.");
        }

        var statementTotal = BankMovementTotal.OfStatementLines(statementLines.Select(x => x.Amount));
        var bookTotal = BankMovementTotal.OfGlLines(bookLines);

        // The sum rule, raised here as a 400 that names a field rather than being left to the
        // aggregate's InvalidOperationException, which would reach the caller as a 500 saying
        // nothing (phase 39). The validator cannot make this check -- it needs both sets of rows --
        // so the handler is the earliest place it can be made, and the Domain check behind it stays
        // as the backstop that no other caller can get round. This is the same division the
        // balanced-GL rule already uses: JournalVoucher's validator gives the 400, GlJournalEntry.Post
        // is the invariant.
        if (statementTotal != bookTotal)
        {
            throw new FluentValidation.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.StatementLineIds),
                    "The selected bank statement lines and book transactions must total the same "
                    + $"amount. The bank side totals {statementTotal.Signed:0.00} and the book side "
                    + $"totals {bookTotal.Signed:0.00}."),
            ]);
        }

        var reconciliation = BankReconciliation.Create(
            request.OrganizationId,
            request.BankAccountId,
            statementTotal,
            statementLines.Count,
            bookTotal,
            bookLines.Count,
            currentUser.UserId,
            DateTimeOffset.UtcNow);

        foreach (var line in statementLines)
        {
            line.Reconcile(reconciliation.Id);
        }

        foreach (var line in bookLines)
        {
            line.Reconcile(reconciliation.Id);
        }

        db.BankReconciliations.Add(reconciliation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBankReconciliationResult(
            reconciliation.Id,
            statementTotal.Signed,
            statementLines.Count,
            bookLines.Count);
    }
}
