using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.CreateBankStatementLine;

public sealed class CreateBankStatementLineCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<CreateBankStatementLineCommand, CreateBankStatementLineResult>
{
    public async Task<CreateBankStatementLineResult> Handle(
        CreateBankStatementLineCommand request, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException("Bank account not found.");

        // A statement belongs to something the tenant holds money in. Kind is phase 17's two-way
        // Bank/Cash toggle, and Other is the rest of the chart of accounts -- importing a bank
        // statement against Sales Revenue is a mistake, not a use case.
        if (account.Kind is not (AccountKind.Bank or AccountKind.Cash))
        {
            throw new ValidationException(
                [new ValidationFailure(
                    nameof(request.BankAccountId),
                    $"'{account.Name}' is not a cash or bank account, so it has no bank statement.")]);
        }

        var line = BankStatementLine.Create(
            request.OrganizationId,
            request.BankAccountId,
            request.Date,
            request.Description,
            ToAmount(request),
            request.ImportJobId,
            timeProvider.GetUtcNow());

        db.BankStatementLines.Add(line);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBankStatementLineResult(
            line.Id, line.Date, line.Description, line.Amount.DepositAmount, line.Amount.WithdrawalAmount);
    }

    /// <summary>
    /// The one place a statement's two columns become a direction.
    ///
    /// <para>The validator rejects both of these cases with a 400 naming the field, so reaching
    /// them means a caller that bypassed it. They stay because the alternative to throwing is
    /// picking a direction, and phase 39's rule is that the Domain check is the backstop while the
    /// validator is what names the field.</para>
    /// </summary>
    private static StatementAmount ToAmount(CreateBankStatementLineCommand request)
    {
        var hasDeposit = request.Deposit != 0m;
        var hasWithdrawal = request.Withdrawal != 0m;

        if (hasDeposit == hasWithdrawal)
        {
            throw new ValidationException(
                [new ValidationFailure(
                    nameof(request.Deposit),
                    hasDeposit
                        ? "A statement line is either a deposit or a withdrawal, not both."
                        : "A statement line must carry a deposit or a withdrawal.")]);
        }

        return hasDeposit
            ? StatementAmount.Deposit(request.Deposit)
            : StatementAmount.Withdrawal(request.Withdrawal);
    }
}
