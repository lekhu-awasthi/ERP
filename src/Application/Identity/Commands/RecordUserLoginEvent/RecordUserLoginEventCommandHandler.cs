using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using MediatR;

namespace ErpApp.Application.Identity.Commands.RecordUserLoginEvent;

public sealed class RecordUserLoginEventCommandHandler(IAppDbContext db)
    : IRequestHandler<RecordUserLoginEventCommand, Unit>
{
    public async Task<Unit> Handle(RecordUserLoginEventCommand request, CancellationToken cancellationToken)
    {
        var loginEvent = UserLoginEvent.Create(
            request.UserId,
            request.Email,
            request.Outcome,
            DateTimeOffset.UtcNow,
            request.IpAddress,
            request.UserAgent,
            UserAgentReader.ReadOperatingSystem(request.UserAgent),
            UserAgentReader.ReadBrowser(request.UserAgent));

        db.UserLoginEvents.Add(loginEvent);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
