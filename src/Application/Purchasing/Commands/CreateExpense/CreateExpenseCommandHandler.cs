using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateExpense;

public sealed class CreateExpenseCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateExpenseCommand, CreateExpenseResult>
{
    public async Task<CreateExpenseResult> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.AccountId), cancellationToken);

        var tdsBaseAmount = request.Lines.Sum(x => x.Amount);
        var tdsAmount = request.TdsApplicable
            ? await PurchasingValidation.ResolveTdsAmountAsync(db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken)
            : 0;

        var expense = Expense.Create(
            request.OrganizationId,
            request.ContactId,
            request.Date,
            request.DueDate,
            request.SupplierInvoiceReference,
            request.Notes,
            request.TdsApplicable,
            request.TdsTypeId,
            tdsAmount);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        expense.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        foreach (var line in request.Lines)
        {
            expense.AddLine(line.AccountId, line.Amount, line.VatRate);
        }

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateExpenseResult(expense.Id, expense.Code, expense.Status);
    }
}
