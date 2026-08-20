using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateBank;

public sealed class UpdateBankCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateBankCommand, UpdateBankResult>
{
    public async Task<UpdateBankResult> Handle(UpdateBankCommand request, CancellationToken cancellationToken)
    {
        var bank = await db.Banks.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Bank not found.");

        var nameTaken = await db.Banks.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A bank named '{request.Name}' already exists.");
        }

        bank.Update(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateBankResult(bank.Id, bank.Name, bank.IsActive);
    }
}
