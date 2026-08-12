using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApproveExpense;

public sealed class ApproveExpenseCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<ExpensePostingInput> postingRule)
    : IRequestHandler<ApproveExpenseCommand, ApproveExpenseResult>
{
    public async Task<ApproveExpenseResult> Handle(ApproveExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Expense not found.");

        if (expense.Status != ExpenseStatus.Draft)
        {
            throw new ConflictException("Only a Draft expense can be approved.");
        }

        if (expense.Lines.Count == 0)
        {
            throw new ConflictException("An expense needs at least one line to be approved.");
        }

        var postingInput = await ExpenseAccountResolver.ResolveAsync(
            db, request.OrganizationId, expense.Lines.Select(x => (x.AccountId, x.Amount, x.VatAmount)),
            expense.TdsAmount, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Expense, cancellationToken);

        expense.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.Expense, expense.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveExpenseResult(expense.Id, expense.Code, expense.Status, expense.ApprovedAt);
    }
}
