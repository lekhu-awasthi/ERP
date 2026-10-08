using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;

public sealed class UpdatePosLocationSettingsCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePosLocationSettingsCommand, PosLocationSettingsDto>
{
    public async Task<PosLocationSettingsDto> Handle(
        UpdatePosLocationSettingsCommand request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        // Only the account whose feature is on is checked: a disabled feature's account is cleared by
        // the aggregate, and refusing a save over a stale id the user can no longer see would be a
        // 404 about a field that is not on the screen.
        var accountIds = new[]
            {
                request.ServiceChargeEnabled ? request.ServiceChargeAccountId : null,
                request.RoundOffEnabled ? request.RoundOffAccountId : null,
            }
            .Where(x => x is not null)
            .Select(x => x!.Value);

        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, accountIds, cancellationToken);

        if (request.DefaultTab is { } tab && !PosTabs.For(location.PosMode).Contains(tab))
        {
            var available = PosTabs.For(location.PosMode);
            throw new ValidationException(
                [new ValidationFailure(
                    nameof(request.DefaultTab),
                    available.Count == 0
                        ? $"'{location.Name}' has no till yet, so it has no default tab. Choose a POS mode first."
                        : $"'{tab}' is not a tab of a {location.PosMode} till. It has: {string.Join(", ", available)}.")]);
        }

        var settings = await db.PosLocationSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == location.Id, cancellationToken);

        if (settings is null)
        {
            settings = PosLocationSettings.CreateDefault(request.OrganizationId, location.Id);
            db.PosLocationSettings.Add(settings);
        }

        settings.Update(
            location.PosMode,
            request.ServiceChargeEnabled,
            request.ServiceChargeRate,
            request.ServiceChargeAccountId,
            request.ServiceChargeOnTakeAway,
            request.RoundOffEnabled,
            request.RoundOffAccountId,
            request.CashVerificationRequired,
            request.Denominations,
            request.DefaultTab,
            request.PrintEstimateBill,
            request.PrintInvoice,
            request.PrintCreditNote,
            request.PrintKot,
            request.AbbreviatedTaxInvoiceEnabled);

        await db.SaveChangesAsync(cancellationToken);

        return await PosLocationSettingsReader.ReadAsync(db, location, cancellationToken);
    }
}
