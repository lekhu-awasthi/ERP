using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Sessions;

public sealed record PosCashMovementDto(
    Guid Id,
    PosCashMovementDirection Direction,
    decimal Amount,
    Guid AccountId,
    string AccountName,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// One session, as the till's X report (while open) or Z report (once closed) reads it.
/// </summary>
/// <param name="ExpectedCash">While open, what the drawer should hold right now. Once closed, the
/// figure frozen at the close -- the one the over/short was posted against.</param>
public sealed record PosSessionDto(
    Guid Id,
    string Code,
    Guid LocationId,
    string LocationName,
    Guid UserId,
    string UserName,
    PosSessionStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    Guid CashAccountId,
    decimal OpeningFloat,
    IReadOnlyList<DenominationCount>? OpeningCount,
    PosSalesSummaryDto Sales,
    IReadOnlyList<PosCashMovementDto> CashMovements,
    decimal CashIn,
    decimal CashOut,
    decimal ExpectedCash,
    decimal? CountedCash,
    IReadOnlyList<DenominationCount>? ClosingCount,
    decimal? CashDifference,
    string? ClosingNote);

/// <summary>Builds <see cref="PosSessionDto"/> from a session loaded with its cash movements, so a
/// command answers with exactly what a reload shows (phase 60's settings-reader rule).</summary>
internal static class PosSessionView
{
    public static async Task<PosSessionDto> ReadAsync(
        IAppDbContext db, PosSession session, CancellationToken cancellationToken)
    {
        var sales = await PosSalesReader.ForSessionAsync(db, session.OrganizationId, session.Id, cancellationToken);

        var locationName = await db.BillingLocations
            .Where(x => x.Id == session.BillingLocationId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "";

        var userName = await db.Users
            .Where(x => x.Id == session.UserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? "";

        var accountIds = session.CashMovements.Select(x => x.AccountId).Distinct().ToList();
        var accountNames = await db.Accounts
            .Where(x => x.OrganizationId == session.OrganizationId && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var movements = session.CashMovements
            .OrderBy(x => x.CreatedAt)
            .Select(x => new PosCashMovementDto(
                x.Id, x.Direction, x.Amount, x.AccountId, accountNames.GetValueOrDefault(x.AccountId, ""), x.Note,
                x.CreatedAt))
            .ToList();

        return new PosSessionDto(
            session.Id,
            session.Code,
            session.BillingLocationId,
            locationName,
            session.UserId,
            userName,
            session.Status,
            session.OpenedAt,
            session.ClosedAt,
            session.CashAccountId,
            session.OpeningFloat,
            session.OpeningCount?.Rows,
            sales,
            movements,
            session.CashMovements.Where(x => x.Direction == PosCashMovementDirection.In).Sum(x => x.Amount),
            session.CashMovements.Where(x => x.Direction == PosCashMovementDirection.Out).Sum(x => x.Amount),
            session.ExpectedCash ?? PosSalesReader.ExpectedCash(session, sales),
            session.CountedCash,
            session.ClosingCount?.Rows,
            session.CashDifference,
            session.ClosingNote);
    }
}
