using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateCreditTerm;

public sealed class CreateCreditTermCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCreditTermCommand, CreateCreditTermResult>
{
    public async Task<CreateCreditTermResult> Handle(CreateCreditTermCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.CreditTerms.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A credit term named '{request.Name}' already exists.");
        }

        var creditTerm = CreditTerm.Create(request.OrganizationId, request.Name, request.DueDays);
        db.CreditTerms.Add(creditTerm);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCreditTermResult(creditTerm.Id, creditTerm.Name, creditTerm.DueDays);
    }
}
