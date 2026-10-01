using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace ErpApp.Application.Pos.Commands.ClosePosSession;

/// <summary>
/// Expected cash comes from <see cref="PosSalesReader"/>, the same reader the session view and the
/// day report read, so the figure a cashier is held to is the figure every report shows. The session
/// row is touched by every sale and carries a rowversion, so a sale committing between this read and
/// this save fails one of the two with a 409 rather than landing in a drawer already counted.
/// </summary>
public sealed class ClosePosSessionCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ClosePosSessionCommand, PosSessionDto>
{
    public async Task<PosSessionDto> Handle(ClosePosSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        var till = await PosTill.LoadSettingsAsync(db, request.OrganizationId, session.BillingLocationId, cancellationToken);

        var (counted, count) = Count(request, till);

        var sales = await PosSalesReader.ForSessionAsync(db, request.OrganizationId, session.Id, cancellationToken);
        var expected = PosSalesReader.ExpectedCash(session, sales);
        var difference = counted - expected;

        // Resolved before anything changes, so a tenant with no over/short account gets a 409 that
        // leaves the session open rather than one that half-closed it.
        var overShortAccountId = difference == 0m
            ? (Guid?)null
            : await PosAccountResolver.CashOverShortAccountAsync(db, request.OrganizationId, cancellationToken);

        try
        {
            session.Close(expected, counted, count, request.Note);
        }
        catch (InvalidOperationException ex)
        {
            // A difference with no note: a 400 on the field that fixes it, not the Domain's 500.
            throw new ValidationException([new ValidationFailure(nameof(request.Note), ex.Message)]);
        }

        if (overShortAccountId is { } overShort)
        {
            // Over: the drawer holds more than the ledger said, so the drawer's account is debited.
            // Short: the other way round, and the shortage is a loss.
            IReadOnlyList<GlLineInput> lines = difference > 0m
                ?
                [
                    new GlLineInput(session.CashAccountId, difference, 0m),
                    new GlLineInput(overShort, 0m, difference),
                ]
                :
                [
                    new GlLineInput(overShort, -difference, 0m),
                    new GlLineInput(session.CashAccountId, 0m, -difference),
                ];

            db.GlJournalEntries.Add(GlJournalEntry.Post(
                request.OrganizationId, DocumentType.PosSession, session.Id, lines, session.BillingLocationId));
        }

        await db.SaveChangesAsync(cancellationToken);

        return await PosSessionView.ReadAsync(db, session, cancellationToken);
    }

    private static (decimal Counted, CashCount? Count) Count(ClosePosSessionCommand request, PosTillContext till)
    {
        if (request.Denominations is { } rows)
        {
            CashCount count;
            try
            {
                count = CashCount.From(rows, till.Settings.Denominations);
            }
            catch (InvalidOperationException ex)
            {
                throw new ValidationException([new ValidationFailure(nameof(request.Denominations), ex.Message)]);
            }

            if (request.CountedAmount is { } amount && amount != count.Total)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.CountedAmount),
                    $"The counted amount ({amount:0.00}) is not what the counted notes add up to ({count.Total:0.00}).")]);
            }

            return (count.Total, count);
        }

        if (till.Settings.CashVerificationRequired)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Denominations),
                $"'{till.Location.Name}' requires cash verification, so the drawer is counted note by note.")]);
        }

        return (request.CountedAmount!.Value, null);
    }
}
