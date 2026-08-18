using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateDealStage;

public sealed class UpdateDealStageCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateDealStageCommand, UpdateDealStageResult>
{
    public async Task<UpdateDealStageResult> Handle(UpdateDealStageCommand request, CancellationToken cancellationToken)
    {
        var dealStage = await db.DealStages.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Deal stage not found.");

        var nameTaken = await db.DealStages.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A deal stage named '{request.Name}' already exists.");
        }

        dealStage.Update(request.Name, request.SortOrder, request.Color, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateDealStageResult(dealStage.Id, dealStage.Name, dealStage.SortOrder, dealStage.Color, dealStage.IsActive);
    }
}
