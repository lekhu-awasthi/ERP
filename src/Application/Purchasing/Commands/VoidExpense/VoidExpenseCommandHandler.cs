using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.VoidExpense;

/// <summary>No stock (Expense has no ProductId lines, only Account lines). GL reversal is a
/// mirror-image entry against the Approve-time posting -- see GlJournalEntry.PostReversalOf's doc
/// comment for why this is foolproof against Phase 6 bug #3's failure mode even with Expense's own
/// TDS-Payable leg.</summary>
public sealed class VoidExpenseCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidExpenseCommand, VoidExpenseResult>
{
    public async Task<VoidExpenseResult> Handle(VoidExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Expense not found.");

        if (expense.Status != ExpenseStatus.Approved)
        {
            throw new ConflictException("Only an Approved expense can be voided.");
        }

        expense.Void(currentUser.UserId);

        // Phase 37 -- reverses whatever is outstanding rather than mirroring one entry. This
        // type posts only one today, but every void in this codebase now asks the same
        // question of the ledger (Application.Accounting.Posting.SourceDocumentGlEntries), so
        // there is no handler left for a second entry to surprise.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.Expense, expense.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidExpenseResult(expense.Id, expense.Code, expense.Status, expense.VoidedAt);
    }
}
