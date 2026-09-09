using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Platform.Commands.SetUserPreference;

public sealed class SetUserPreferenceCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetUserPreferenceCommand, UserPreferenceDto>
{
    public async Task<UserPreferenceDto> Handle(
        SetUserPreferenceCommand request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Upsert against the (OrganizationId, UserId, Key) unique index. Two tabs saving the same
        // setting race to the same row and the later one wins, which is the intended semantics for a
        // personal setting; two tabs saving *different* settings cannot collide at all, which is the
        // whole reason this is a row per key rather than one document per user.
        var existing = await db.UserPreferences.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.UserId == currentUser.UserId
                 && x.Key == request.Key,
            cancellationToken);

        if (existing is null)
        {
            existing = UserPreference.Create(
                request.OrganizationId, currentUser.UserId, request.Key, request.Value, now);
            db.UserPreferences.Add(existing);
        }
        else
        {
            existing.SetValue(request.Value, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new UserPreferenceDto(existing.Key, existing.Value, existing.UpdatedAt);
    }
}
