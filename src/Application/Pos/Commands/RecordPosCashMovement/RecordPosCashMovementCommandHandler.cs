using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace ErpApp.Application.Pos.Commands.RecordPosCashMovement;

public sealed class RecordPosCashMovementCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, ICashBalancePolicy cashBalancePolicy)
    : IRequestHandler<RecordPosCashMovementCommand, PosSessionDto>
{
    public async Task<PosSessionDto> Handle(RecordPosCashMovementCommand request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);

        if (request.AccountId == session.CashAccountId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.AccountId),
                "Name where the cash came from or went to. It cannot be the drawer's own account.")]);
        }

        // Phase 31 -- an outflow from a cash or bank account. Out takes it from the drawer; In takes
        // it from wherever it came from, which the policy ignores unless that is a cash or bank account
        // too (a float topped up from the bank).
        var outflowAccountId = request.Direction == PosCashMovementDirection.Out ? session.CashAccountId : request.AccountId;
        var cashStatus = await cashBalancePolicy.CheckAsync(
            request.OrganizationId, [new CashOutflow(outflowAccountId, request.Amount)], cancellationToken);
        CashBalanceGuard.Enforce(cashStatus, request.OverrideNegativeCashBalanceWarning, "cash movement");

        var movement = session.RecordCashMovement(
            request.Direction, request.Amount, request.AccountId, request.Note, currentUser.UserId);
        db.PosCashMovements.Add(movement);

        IReadOnlyList<GlLineInput> lines = request.Direction == PosCashMovementDirection.In
            ?
            [
                new GlLineInput(session.CashAccountId, request.Amount, 0m),
                new GlLineInput(request.AccountId, 0m, request.Amount),
            ]
            :
            [
                new GlLineInput(request.AccountId, request.Amount, 0m),
                new GlLineInput(session.CashAccountId, 0m, request.Amount),
            ];

        // Sourced to the session, so a GL report names the drawer it came from (SES0001), and stamped
        // with its location like every other posting since phase 35b.
        db.GlJournalEntries.Add(GlJournalEntry.Post(
            request.OrganizationId, DocumentType.PosSession, session.Id, lines, session.BillingLocationId));

        await db.SaveChangesAsync(cancellationToken);

        return await PosSessionView.ReadAsync(db, session, cancellationToken);
    }
}
