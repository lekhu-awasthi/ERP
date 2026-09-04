using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateCurrency;

public sealed class UpdateCurrencyCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCurrencyCommand, UpdateCurrencyResult>
{
    public async Task<UpdateCurrencyResult> Handle(UpdateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var currency = await db.Currencies.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Currency not found.");

        try
        {
            currency.Update(request.Name, request.Symbol, request.IsActive);
        }
        catch (InvalidOperationException ex)
        {
            // Deactivating the base currency. A 409 rather than the Domain exception's 500, same
            // mapping every other "the aggregate said no" path in this codebase uses.
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCurrencyResult(currency.Id, currency.Code, currency.Name, currency.Symbol, currency.IsActive);
    }
}
