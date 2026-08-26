using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateCostTerm;

public sealed class CreateCostTermCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCostTermCommand, CreateCostTermResult>
{
    public async Task<CreateCostTermResult> Handle(CreateCostTermCommand request, CancellationToken cancellationToken)
    {
        // Uniqueness is per (organization, category, name), not per organization -- the two
        // categories are separate sections in the reference product's own screen, so "Freight"
        // existing as an additional-cost term must not block it as a production-cost term. Same
        // shape as CustomStatus's per-DocumentType uniqueness.
        var nameExists = await db.CostTerms.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Category == request.Category
                 && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException(
                $"A cost term named '{request.Name}' already exists for {request.Category}.");
        }

        var costTerm = CostTerm.Create(request.OrganizationId, request.Name, request.Category);
        db.CostTerms.Add(costTerm);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCostTermResult(costTerm.Id, costTerm.Name, costTerm.Category);
    }
}
