using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.OpenPosSession;

public sealed class OpenPosSessionCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<OpenPosSessionCommand, PosSessionDto>
{
    public async Task<PosSessionDto> Handle(OpenPosSessionCommand request, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);

        // Decision G -- a drawer only exists where its owner may sell. Pos.Session.Operate is
        // organization-wide, so this is what keeps a cashier scoped to one branch (phase 32b) from
        // opening a drawer, and moving cash through the ledger, at another.
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceCreate, till.Location.Id,
            cancellationToken);

        var alreadyOpen = await db.PosSessions
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == till.Location.Id
                && x.UserId == currentUser.UserId && x.Status == PosSessionStatus.Open)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (alreadyOpen is not null)
        {
            throw new ConflictException(
                $"You already have session {alreadyOpen} open at '{till.Location.Name}'. Close it before opening another.");
        }

        var drawerAccountId = await PosTill.DrawerAccountAsync(db, request.OrganizationId, till.Location, cancellationToken);

        var (openingFloat, openingCount) = CountFloat(request, till);

        var number = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.PosSession, cancellationToken);

        var session = PosSession.Open(
            request.OrganizationId, till.Location.Id, currentUser.UserId, drawerAccountId,
            PosSession.CodePrefix + number, openingFloat, openingCount);

        db.PosSessions.Add(session);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a second tab opening at the same moment -- the check
            // above cannot, because both requests passed it before either saved. Anything else is
            // not ours to explain, so it goes on up.
            if (!await HasOpenSessionAsync(request, cancellationToken))
            {
                throw;
            }

            throw new ConflictException(
                $"You already have a session open at '{till.Location.Name}'. Close it before opening another.");
        }

        return await PosSessionView.ReadAsync(db, session, cancellationToken);
    }

    private static (decimal Float, CashCount? Count) CountFloat(OpenPosSessionCommand request, PosTillContext till)
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

            if (request.OpeningAmount is { } amount && amount != count.Total)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.OpeningAmount),
                    $"The opening amount ({amount:0.00}) is not what the counted notes add up to ({count.Total:0.00}).")]);
            }

            return (count.Total, count);
        }

        if (till.Settings.CashVerificationRequired)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Denominations),
                $"'{till.Location.Name}' requires cash verification, so its float is counted note by note.")]);
        }

        return (request.OpeningAmount ?? 0m, null);
    }

    private Task<bool> HasOpenSessionAsync(OpenPosSessionCommand request, CancellationToken cancellationToken) =>
        db.PosSessions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == request.LocationId
                && x.UserId == currentUser.UserId && x.Status == PosSessionStatus.Open,
            cancellationToken);
}
