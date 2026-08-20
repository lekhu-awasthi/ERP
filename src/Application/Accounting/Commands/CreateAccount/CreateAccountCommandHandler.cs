using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.CreateAccount;

public sealed class CreateAccountCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<CreateAccountCommand, CreateAccountResult>
{
    public async Task<CreateAccountResult> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var group = await db.AccountGroups.SingleOrDefaultAsync(
            x => x.Id == request.GroupId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Account group not found.");

        var nameExists = await db.Accounts.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"An account named '{request.Name}' already exists.");
        }

        await AccountingValidation.EnsureBankExistsAsync(db, request.OrganizationId, request.BankId, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Account, cancellationToken);

        var account = Account.Create(
            request.OrganizationId, code, request.Name, group.RootType, request.GroupId,
            request.Kind, request.BankId, request.AccountNumber);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateAccountResult(
            account.Id, account.Code, account.Name, account.RootType, account.GroupId,
            account.Kind, account.BankId, account.AccountNumber);
    }
}
