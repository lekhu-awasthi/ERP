using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateCostTerm;

public sealed class UpdateCostTermCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCostTermCommand, UpdateCostTermResult>
{
    public async Task<UpdateCostTermResult> Handle(UpdateCostTermCommand request, CancellationToken cancellationToken)
    {
        var costTerm = await db.CostTerms.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cost term not found.");

        var nameTaken = await db.CostTerms.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Id != request.Id
                 && x.Category == request.Category
                 && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException(
                $"A cost term named '{request.Name}' already exists for {request.Category}.");
        }

        costTerm.Update(request.Name, request.Category, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCostTermResult(costTerm.Id, costTerm.Name, costTerm.Category, costTerm.IsActive);
    }
}
