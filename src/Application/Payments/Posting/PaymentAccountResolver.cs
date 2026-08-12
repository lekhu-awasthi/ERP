using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Shared by ApprovePaymentCommandHandler and the Payment PreviewGlPostingQuery handler
/// -- resolves TenantSettings' DefaultAccountsReceivableId (Direction=Received) or
/// DefaultAccountsPayableId (Direction=Paid) (cashOrBankAccountId/amount are already known to the
/// caller, no resolution needed) into the pure PaymentPostingInput PaymentPostingRule consumes.
/// Same "friendly ConflictException, not a Domain 500" precedent as
/// Sales.Posting.InvoiceAccountResolver.</summary>
internal static class PaymentAccountResolver
{
    public static async Task<PaymentPostingInput> ResolveAsync(
        IAppDbContext db, Guid organizationId, Guid cashOrBankAccountId, decimal amount, PaymentDirection direction,
        CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var controlAccountId = direction == PaymentDirection.Received
            ? settings.DefaultAccountsReceivableId
                ?? throw new ConflictException(
                    "Default Accounts Receivable account is not configured. Set it under Accounting Defaults before approving payments.")
            : settings.DefaultAccountsPayableId
                ?? throw new ConflictException(
                    "Default Accounts Payable account is not configured. Set it under Accounting Defaults before approving payments.");

        return new PaymentPostingInput(cashOrBankAccountId, controlAccountId, amount, direction);
    }
}
