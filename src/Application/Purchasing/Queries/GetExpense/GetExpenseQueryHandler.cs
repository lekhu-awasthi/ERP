using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetExpense;

public sealed class GetExpenseQueryHandler(IAppDbContext db) : IRequestHandler<GetExpenseQuery, ExpenseDetailDto>
{
    public async Task<ExpenseDetailDto> Handle(GetExpenseQuery request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Expense not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (expense.Status == ExpenseStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.Expense, expense.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new ExpenseDetailDto(
            expense.Id,
            expense.OrganizationId,
            expense.ContactId,
            expense.Code,
            expense.Date,
            expense.DueDate,
            expense.SupplierInvoiceReference,
            expense.Notes,
            expense.TdsApplicable,
            expense.TdsTypeId,
            expense.TdsAmount,
            expense.Status,
            expense.ApprovedByUserId,
            expense.ApprovedAt,
            expense.CreatedAt,
            expense.GrandTotal,
            expense.Lines.Select(x => new ExpenseLineDto(x.Id, x.AccountId, x.Amount, x.VatRate, x.VatAmount)).ToList(),
            glLines,
            expense.CurrencyCode,
            expense.ExchangeRate,
            expense.LocationId);
    }
}
