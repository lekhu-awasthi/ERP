using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Sessions;

/// <summary>
/// Phase 61 -- who may act on a session. A session is one cashier's drawer: only its owner rings
/// sales into it, moves its cash or closes it, because the count at the close is that person's
/// accountability. Reading it is wider -- <see cref="PermissionKeys.PosSessionViewAll"/> reads anyone's.
/// </summary>
internal static class PosSessionAccess
{
    /// <summary>The caller's own session, tracked, with its cash movements. 404 for a session that is
    /// not this organization's, 403 for someone else's, 409 for a closed one.</summary>
    public static async Task<PosSession> LoadOwnOpenAsync(
        IAppDbContext db, Guid organizationId, Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await db.PosSessions
            .Include(x => x.CashMovements)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Session not found.");

        if (session.UserId != userId)
        {
            throw new ForbiddenException(
                $"Session {session.Code} is another cashier's drawer. Only they can sell in it, move its cash or close it.");
        }

        if (session.Status != PosSessionStatus.Open)
        {
            throw new ConflictException($"Session {session.Code} is closed.");
        }

        return session;
    }

    /// <summary>Any session the caller may read: their own, or anyone's with
    /// <see cref="PermissionKeys.PosSessionViewAll"/>. The refusal is the pipeline's own wording, so
    /// whether the behaviour or this re-check refused cannot be told apart (phase 27a).</summary>
    public static async Task<PosSession> LoadReadableAsync(
        IAppDbContext db, Guid organizationId, Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await db.PosSessions
            .AsNoTracking()
            .Include(x => x.CashMovements)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Session not found.");

        if (session.UserId != userId)
        {
            await GrantedPermissionReader.EnsureGrantedAsync(
                db, organizationId, userId, PermissionKeys.PosSessionViewAll, cancellationToken);
        }

        return session;
    }
}
