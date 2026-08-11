using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateCashTransfer;

public sealed class CreateCashTransferCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCashTransferCommand, CreateCashTransferResult>
{
    public async Task<CreateCashTransferResult> Handle(CreateCashTransferCommand request, CancellationToken cancellationToken)
    {
        var accountIds = request.Lines.Select(x => x.ToAccountId).Append(request.FromAccountId);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, accountIds, cancellationToken);

        var cashTransfer = CashTransfer.Create(request.OrganizationId, request.Date, request.Reference, request.FromAccountId);
        foreach (var line in request.Lines)
        {
            cashTransfer.AddLine(line.ToAccountId, line.Amount);
        }

        db.CashTransfers.Add(cashTransfer);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status);
    }
}
