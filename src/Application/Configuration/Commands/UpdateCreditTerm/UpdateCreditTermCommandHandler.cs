using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateCreditTerm;

public sealed class UpdateCreditTermCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCreditTermCommand, UpdateCreditTermResult>
{
    public async Task<UpdateCreditTermResult> Handle(UpdateCreditTermCommand request, CancellationToken cancellationToken)
    {
        var creditTerm = await db.CreditTerms.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit term not found.");

        var nameTaken = await db.CreditTerms.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A credit term named '{request.Name}' already exists.");
        }

        creditTerm.Update(request.Name, request.DueDays, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCreditTermResult(creditTerm.Id, creditTerm.Name, creditTerm.DueDays, creditTerm.IsActive);
    }
}
