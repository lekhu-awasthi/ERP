using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;

public sealed class SetPosLocationPaymentModesCommandHandler(IAppDbContext db)
    : IRequestHandler<SetPosLocationPaymentModesCommand, PosLocationSettingsDto>
{
    public async Task<PosLocationSettingsDto> Handle(
        SetPosLocationPaymentModesCommand request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        var wanted = request.PaymentModeIds.Distinct().ToList();

        if (wanted.Count > 0)
        {
            var modes = await db.PaymentModes
                .Where(x => x.OrganizationId == request.OrganizationId && wanted.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, x.IsActive, x.AccountId })
                .ToListAsync(cancellationToken);

            if (modes.Count != wanted.Count)
            {
                throw new NotFoundException("One or more payment modes were not found.");
            }

            var unusable = modes.Where(x => !x.IsActive || x.AccountId is null).Select(x => x.Name).OrderBy(x => x).ToList();
            if (unusable.Count > 0)
            {
                throw new ValidationException(
                    [new ValidationFailure(
                        nameof(request.PaymentModeIds),
                        $"A payment mode offered at a till must be active and name its payment account: "
                        + $"{string.Join(", ", unusable.Select(x => $"'{x}'"))}. Set it on Configurations > Payment Modes.")]);
            }
        }

        // Through the child DbSet, diffed: remove what is no longer wanted, add what is new. Never a
        // clear-and-re-add of the whole set (phase-4 bug #1).
        var existing = await db.PosLocationPaymentModes
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == location.Id)
            .ToListAsync(cancellationToken);

        db.PosLocationPaymentModes.RemoveRange(existing.Where(x => !wanted.Contains(x.PaymentModeId)));
        db.PosLocationPaymentModes.AddRange(
            wanted
                .Where(id => existing.All(x => x.PaymentModeId != id))
                .Select(id => PosLocationPaymentMode.Create(request.OrganizationId, location.Id, id)));

        await db.SaveChangesAsync(cancellationToken);

        return await PosLocationSettingsReader.ReadAsync(db, location, cancellationToken);
    }
}
