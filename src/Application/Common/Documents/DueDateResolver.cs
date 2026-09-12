using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Documents;

/// <summary>
/// The due date a document gets when its caller did not name one: the contact's Credit Term applied
/// to the document's own date, or the document date itself when the contact has no term.
///
/// <para><b>Phase 36 -- why this moved to the server.</b> Phase 31 gave Invoice and PurchaseBill a
/// stored Due Date and seeded it from <c>Contact.CreditTermId</c> <i>in the browser</i>, when the
/// user picks a contact on the form. That made the term real for anyone using the UI and invisible
/// to everyone else: a document created straight through the API, by an import, by a conversion or
/// by a background job got a due date equal to its document date even for a contact on 30-day
/// terms -- and the ageing reports, which bucket from exactly this field, then aged it as if it had
/// fallen due the day it was raised. One rule, applied wherever a document is written, is what
/// makes the term mean something (phase 31's own lesson: a tenant-level field is only real if you
/// can name the command that writes it).</para>
///
/// <para><b>It is a default, not a derivation.</b> An explicit due date always wins, including one
/// equal to the document date -- confirmed live in phase 31: the reference product's Invoice form
/// carries a freely editable Due Date and no Credit Terms field at all, and its Invoice Age report
/// shows intervals no configured term could produce. The term seeds the field; it never owns
/// it.</para>
/// </summary>
internal static class DueDateResolver
{
    /// <summary>
    /// <paramref name="requested"/> when the caller named one; otherwise
    /// <paramref name="documentDate"/> plus the contact's <c>CreditTerm.DueDays</c>, or
    /// <paramref name="documentDate"/> itself when the contact has no term.
    /// </summary>
    public static async Task<DateOnly> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid contactId,
        DateOnly documentDate,
        DateOnly? requested,
        CancellationToken cancellationToken)
    {
        if (requested is { } explicitDueDate)
        {
            return explicitDueDate;
        }

        var dueDays = await db.Contacts
            .Where(x => x.Id == contactId && x.OrganizationId == organizationId && x.CreditTermId != null)
            .Join(
                db.CreditTerms.Where(t => t.OrganizationId == organizationId),
                contact => contact.CreditTermId,
                term => term.Id,
                (_, term) => (int?)term.DueDays)
            .SingleOrDefaultAsync(cancellationToken);

        // A term of zero days is a real answer ("due on receipt") and reads the same as no term.
        return dueDays is { } days ? documentDate.AddDays(days) : documentDate;
    }
}
