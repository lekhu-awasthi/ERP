using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosSession;

public sealed class GetPosSessionQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPosSessionQuery, PosSessionDto>
{
    public async Task<PosSessionDto> Handle(GetPosSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadReadableAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        return await PosSessionView.ReadAsync(db, session, cancellationToken);
    }
}
