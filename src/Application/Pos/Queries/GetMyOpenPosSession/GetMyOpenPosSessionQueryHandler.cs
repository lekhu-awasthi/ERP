using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetMyOpenPosSession;

public sealed class GetMyOpenPosSessionQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOpenPosSessionQuery, PosSessionDto?>
{
    public async Task<PosSessionDto?> Handle(GetMyOpenPosSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await db.PosSessions
            .AsNoTracking()
            .Include(x => x.CashMovements)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == request.LocationId
                    && x.UserId == currentUser.UserId && x.Status == PosSessionStatus.Open,
                cancellationToken);

        return session is null ? null : await PosSessionView.ReadAsync(db, session, cancellationToken);
    }
}
