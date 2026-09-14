using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateOrganization;

public sealed class UpdateOrganizationCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateOrganizationCommand, UpdateOrganizationResult>
{
    public async Task<UpdateOrganizationResult> Handle(
        UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(
            x => x.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        // The one guard this command needs, and it is about a date rather than a permission.
        // CreateOrUpdateOpeningStockLineCommandHandler stamps the organization's AccountingStartDate
        // onto the FIFO layer and the StockMovement row it writes, and nothing ever restates those.
        // So moving the start date after opening stock exists leaves the layers dated to the old day
        // zero while every report cutting off at the new one disagrees with them -- phase-37's rule
        // that a stock value has to reach all three views or two of them drift quietly, arriving
        // through the date instead of through the amount. Refusing is the honest answer: the tenant
        // can still correct every other detail, and a genuine cutover-date change means the opening
        // stock is wrong too and has to be re-entered anyway.
        if (request.AccountingStartDate != organization.AccountingStartDate)
        {
            var hasOpeningStock = await db.OpeningStockLines
                .AnyAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken);

            if (hasOpeningStock)
            {
                throw new ConflictException(
                    "The accounting start date cannot be changed once opening stock has been entered, "
                        + "because the existing stock layers are dated to the current one. Remove the "
                        + "opening stock first.");
            }
        }

        organization.UpdateDetails(
            request.Name,
            request.Industry,
            request.Address,
            request.AccountingStartDate,
            request.IsVatRegistered,
            request.Email,
            request.Phone,
            request.PanNumber,
            request.Website);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateOrganizationResult(organization.Id, organization.Name);
    }
}
