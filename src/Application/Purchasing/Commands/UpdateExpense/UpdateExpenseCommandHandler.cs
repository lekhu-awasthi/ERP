using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.UpdateExpense;

public sealed class UpdateExpenseCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateExpenseCommand, UpdateExpenseResult>
{
    public async Task<UpdateExpenseResult> Handle(UpdateExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Expense not found.");

        if (expense.Status != ExpenseStatus.Draft)
        {
            throw new ConflictException("Only a Draft expense can be edited.");
        }

        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.AccountId), cancellationToken);

        var tdsBaseAmount = request.Lines.Sum(x => x.Amount);
        var tdsAmount = request.TdsApplicable
            ? await PurchasingValidation.ResolveTdsAmountAsync(db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken)
            : 0;

        var oldLines = expense.Lines.ToList();

        expense.UpdateHeader(
            request.ContactId,
            request.Date,
            request.DueDate,
            request.SupplierInvoiceReference,
            request.Notes,
            request.TdsApplicable,
            request.TdsTypeId,
            tdsAmount);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        expense.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        expense.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.Expense, request.LocationId,
            cancellationToken));

        expense.ClearLines();
        foreach (var line in request.Lines)
        {
            expense.AddLine(line.AccountId, line.Amount, line.VatRate);
        }

        db.ExpenseLines.RemoveRange(oldLines);
        db.ExpenseLines.AddRange(expense.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateExpenseResult(expense.Id, expense.Code, expense.Status);
    }
}
