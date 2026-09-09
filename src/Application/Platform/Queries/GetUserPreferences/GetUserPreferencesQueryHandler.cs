using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Platform.Commands.SetUserPreference;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Platform.Queries.GetUserPreferences;

public sealed class GetUserPreferencesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetUserPreferencesQuery, IReadOnlyList<UserPreferenceDto>>
{
    public async Task<IReadOnlyList<UserPreferenceDto>> Handle(
        GetUserPreferencesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.UserPreferences
            .Where(x => x.OrganizationId == request.OrganizationId && x.UserId == currentUser.UserId)
            .OrderBy(x => x.Key)
            .Select(x => new UserPreferenceDto(x.Key, x.Value, x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
