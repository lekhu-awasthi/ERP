using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.UpdateAccount;

public sealed class UpdateAccountCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateAccountCommand, UpdateAccountResult>
{
    public async Task<UpdateAccountResult> Handle(UpdateAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Account not found.");

        var group = await db.AccountGroups.SingleOrDefaultAsync(
            x => x.Id == request.GroupId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Account group not found.");

        var nameTaken = await db.Accounts.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"An account named '{request.Name}' already exists.");
        }

        await AccountingValidation.EnsureBankExistsAsync(db, request.OrganizationId, request.BankId, cancellationToken);

        account.Update(request.Name, request.GroupId, group.RootType, request.IsActive, request.Kind, request.BankId, request.AccountNumber);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateAccountResult(
            account.Id, account.Name, account.RootType, account.GroupId, account.IsActive,
            account.Kind, account.BankId, account.AccountNumber);
    }
}
