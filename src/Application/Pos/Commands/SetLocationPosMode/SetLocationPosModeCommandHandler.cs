using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.SetLocationPosMode;

public sealed class SetLocationPosModeCommandHandler(IAppDbContext db)
    : IRequestHandler<SetLocationPosModeCommand, PosLocationSettingsDto>
{
    public async Task<PosLocationSettingsDto> Handle(SetLocationPosModeCommand request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        // Phase 61 -- the same question the till asks before every request, so the two cannot drift.
        await PosTill.EnsureEntitledAsync(db, request.OrganizationId, request.PosMode, cancellationToken);

        try
        {
            location.SetPosMode(request.PosMode);
        }
        catch (InvalidOperationException ex)
        {
            // An inactive location given a till. A 409 naming the reason, not the Domain's 500.
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await PosLocationSettingsReader.ReadAsync(db, location, cancellationToken);
    }
}
