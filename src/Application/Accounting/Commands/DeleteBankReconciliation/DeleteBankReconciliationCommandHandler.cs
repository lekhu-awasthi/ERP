using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.DeleteBankReconciliation;

public sealed class DeleteBankReconciliationCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteBankReconciliationCommand, DeleteBankReconciliationResult>
{
    public async Task<DeleteBankReconciliationResult> Handle(
        DeleteBankReconciliationCommand request, CancellationToken cancellationToken)
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
            .ToListAsync(cancellationToken);

        // The book side again goes through its entry for the tenant filter -- GlLine carries no
        // OrganizationId. The reconciliation id is already unique to this tenant's row, but the
        // predicate is not left implicit: there is no global query filter in this codebase, and a
        // reader has to be able to see the boundary without tracing where the id came from.
        var bookLines = await db.GlLines
            .Where(x => x.ReconciliationId == request.ReconciliationId)
            .Join(
                db.GlJournalEntries.Where(e => e.OrganizationId == request.OrganizationId),
                line => line.GlJournalEntryId,
                entry => entry.Id,
                (line, _) => line)
            .ToListAsync(cancellationToken);

        foreach (var line in statementLines)
        {
            line.ReleaseFromReconciliation();
        }

        foreach (var line in bookLines)
        {
            line.ReleaseFromReconciliation();
        }

        db.BankReconciliations.Remove(reconciliation);
        await db.SaveChangesAsync(cancellationToken);

        return new DeleteBankReconciliationResult(statementLines.Count, bookLines.Count);
    }
}
