using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration;

/// <summary>
/// Phase 60 -- a payment mode's account is a cash or bank account (phase 17's <see cref="AccountKind"/>),
/// the vendor's "Payment Account" list. A tender settles money into a drawer or a bank, so an income
/// or expense account there would post a sale's settlement as a second sale.
/// </summary>
internal static class PaymentModeAccountRule
{
    public static async Task EnsureCashOrBankAsync(
        IAppDbContext db, Guid organizationId, Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId is not { } id)
        {
            return;
        }

        var kind = await db.Accounts
            .Where(x => x.Id == id && x.OrganizationId == organizationId)
            .Select(x => (AccountKind?)x.Kind)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Payment account not found.");

        if (kind is not (AccountKind.Cash or AccountKind.Bank))
        {
            throw new ValidationException(
                [new ValidationFailure(
                    "AccountId", "A payment mode's account must be a cash or bank account.")]);
        }
    }
}
