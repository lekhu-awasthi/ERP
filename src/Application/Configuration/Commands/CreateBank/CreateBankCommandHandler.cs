using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateBank;

public sealed class CreateBankCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateBankCommand, CreateBankResult>
{
    public async Task<CreateBankResult> Handle(CreateBankCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.Banks.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A bank named '{request.Name}' already exists.");
        }

        var bank = Bank.Create(request.OrganizationId, request.Name);
        db.Banks.Add(bank);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateBankResult(bank.Id, bank.Name);
    }
}
