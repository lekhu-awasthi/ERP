using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
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

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        cashTransfer.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        cashTransfer.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.CashTransfer, request.LocationId,
            cancellationToken));
        foreach (var line in request.Lines)
        {
            cashTransfer.AddLine(line.ToAccountId, line.Amount);
        }

        db.CashTransfers.Add(cashTransfer);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status);
    }
}
