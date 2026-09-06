using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Credit;

/// <summary>
/// Phase 31's implementation of <see cref="ICreditLimitPolicy"/>.
///
/// <para><b>It reads the balance through <c>ContactLedgerReader</c>, not through the general
/// ledger.</b> That is phase-26b's shared-reader discipline applied here: the number this check
/// compares against is by construction the same number the Contact Overview widget and the Customer
/// Statement show, so a user who is told "26,800 exceeds your limit" can open the contact and find
/// 26,800. Deriving it independently from <c>GlLine</c> would have produced a second answer to the
/// same question, which is exactly what that discipline exists to prevent.</para>
///
/// <para><b>Customers only.</b> The tenant setting's own wording is "when a <i>Customer's</i>
/// balance is about to exceed it's credit limit", and the only caller is Invoice Approve. A supplier
/// carries a CreditLimit because the live form offers one, but nothing reads it -- see
/// docs/phase-31-status.md Decision B. A non-Customer contact returns Ok here rather than throwing,
/// so a mis-typed document fails on its own terms rather than on this one.</para>
///
/// <para><b>Currency.</b> <paramref name="documentAmount"/> is taken in the document's own currency
/// and is deliberately <i>not</i> folded to base, because <c>ContactLedgerReader</c> sums its events
/// un-converted too. Converting only the new document would compare a base-currency figure against a
/// mixed-currency running total and produce a number matching neither the Statement nor the
/// Overview. This is inherited from phase 28's carried limitation (the contact-ledger family has no
/// currency fold at all), not a new one, and it is exact on a single-currency tenant -- which is
/// every tenant the reference product can currently produce.</para>
/// </summary>
public sealed class ContactCreditLimitPolicy(IAppDbContext db) : ICreditLimitPolicy
{
    public async Task<CreditLimitCheckResult> CheckAsync(
        Guid organizationId, Guid contactId, decimal documentAmount, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == contactId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        // Zero is "no limit" (Contact.CreditLimit), so there is nothing to check and no ledger to
        // load -- which also means this whole check costs a fresh tenant one already-loaded row.
        if (contact.CreditLimit <= 0m || contact.Type != ContactType.Customer)
        {
            return new CreditLimitCheckResult(
                CreditLimitStatus.Ok, contact.Name, contact.Code, 0m, contact.CreditLimit);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await ContactLedgerReader.LoadEventsAsync(
            db, organizationId, ContactType.Customer, contactId, today, cancellationToken);

        var projectedBalance = contact.OpeningBalance + events.Sum(x => x.SignedAmount) + documentAmount;

        if (projectedBalance <= contact.CreditLimit)
        {
            return new CreditLimitCheckResult(
                CreditLimitStatus.Ok, contact.Name, contact.Code, projectedBalance, contact.CreditLimit);
        }

        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var status = settings.CreditLimitExceedsAction switch
        {
            BalanceAction.Reject => CreditLimitStatus.Reject,
            BalanceAction.DoNothing => CreditLimitStatus.Ok,
            _ => CreditLimitStatus.Warn,
        };

        return new CreditLimitCheckResult(
            status, contact.Name, contact.Code, projectedBalance, contact.CreditLimit);
    }
}
