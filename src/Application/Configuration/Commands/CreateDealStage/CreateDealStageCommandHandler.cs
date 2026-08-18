using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateDealStage;

public sealed class CreateDealStageCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateDealStageCommand, CreateDealStageResult>
{
    public async Task<CreateDealStageResult> Handle(CreateDealStageCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.DealStages.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A deal stage named '{request.Name}' already exists.");
        }

        var dealStage = DealStage.Create(request.OrganizationId, request.Name, request.SortOrder, request.Color);
        db.DealStages.Add(dealStage);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDealStageResult(dealStage.Id, dealStage.Name, dealStage.SortOrder, dealStage.Color);
    }
}
