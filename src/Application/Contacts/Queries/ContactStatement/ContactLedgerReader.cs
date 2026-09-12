using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

/// <summary>
/// The event-loading + signed-delta computation every contact-balance report needs -- extracted
/// here rather than duplicated once a second caller appeared. Kept as static functions over an
/// explicit parameter list, not an instance/DI service: there's no state to hold, and a static
/// function is the smallest thing that satisfies its callers without inventing a new abstraction
/// layer for what's still just a handful of lines (see phase-10-status.md's reuse-vs-duplicate call).
///
/// <para><b>Callers:</b> <c>ContactStatementQueryHandler</c>, <c>ContactOverviewQueryHandler</c>,
/// and (phase 26b) <c>ContactBalanceSummaryQueryHandler</c> -- which is why the per-contact load
/// grew an all-contacts sibling rather than being called in a loop.</para>
///
/// <para><b>Phase 26b -- Journal Vouchers are ledger events.</b> A <c>JournalVoucherLine</c> tagged
/// with a ContactId posts against that contact's own AR/AP control account (its own doc comment
/// says so, and phase-17 built the tagging for exactly that). Until this phase nothing read those
/// lines back, so a JV posted to a customer moved the general ledger without moving that customer's
/// Statement -- and the 2026-09-03 confirm-live pass settled the question the other way round:
/// the reference product's Invoice Age lists Journal Vouchers as ageable documents beside invoices,
/// with its Txn Type filter naming Journal Voucher explicitly. Including them here fixes Contact
/// Statement and Contact Overview as a side effect, which is deliberate; see
/// docs/phase-26b-status.md's Decision B for what changes and why that is a correction rather than
/// a scope creep.</para>
///
/// <para><b>Phase 36 -- every amount is folded to the base currency</b>, each document at its own
/// stored rate. A contact's balance is one number, so a ledger that summed a USD invoice and an NPR
/// receipt as though they were the same unit was not a balance at all -- and
/// <c>ContactCreditLimitPolicy</c> compared that mixed sum against a base-currency credit limit
/// (phase-31 carried item #3, inherited from phase 28's carried limitation on this whole family).
/// A single-currency tenant is untouched: every rate is 1.</para>
///
/// <para><b>A settlement is folded at the rate of what it settles</b>, not at its own. A payment's
/// allocated portion converts at each target document's rate and only its unallocated remainder at
/// the payment's own rate. Folding the whole payment at its own rate would leave a fully settled
/// invoice showing a residual balance equal to the realised exchange difference -- which is a real
/// number, but it belongs in the P&amp;L, where <c>PaymentForexCalculator</c> books it, and not in
/// what a customer still owes. This is the same rule the ageing reports follow by folding each
/// document's net at that document's rate, which is why the two still agree.</para>
///
/// Each line set is loaded with its own concrete Where lambda, not a generic helper over
/// IQueryable&lt;TLine&gt; -- a generic parent-id selector passed as a captured Func can't be
/// translated by EF Core's LINQ provider, the same gotcha phase-9-status.md already hit once here.
/// </summary>
internal static class ContactLedgerReader
{
    internal sealed record Event(
        Guid ContactId,
        DateOnly Date,
        DocumentType DocumentType,
        string Code,
        string? Reference,
        decimal SignedAmount);

    /// <summary>Every ledger event for one contact up to <paramref name="toDate"/>.
    ///
    /// <para><b>Phase 35b -- the two location arguments.</b> <paramref name="locationId"/> is the
    /// report's own Billing Location filter and <paramref name="reportLocations"/> is the caller's
    /// permission scope; both default to null, which is "All locations" and "unrestricted" and is
    /// what every pre-35b caller keeps getting. They narrow the <b>documents</b> an event is built
    /// from, because that is the only place a location exists -- a contact carries none, and neither
    /// does their balance.</para></summary>
    internal static Task<List<Event>> LoadEventsAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, Guid contactId, DateOnly toDate,
        CancellationToken cancellationToken, Guid? locationId = null, IReadOnlyList<Guid>? reportLocations = null) =>
        LoadAsync(db, organizationId, contactType, contactId, toDate, cancellationToken, locationId, reportLocations);

    /// <summary>Every ledger event for <b>every</b> contact of this type up to
    /// <paramref name="toDate"/> -- what a per-contact summary needs, in the same number of round
    /// trips one contact costs rather than one round trip per contact.</summary>
    internal static Task<List<Event>> LoadAllContactEventsAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, DateOnly toDate,
        CancellationToken cancellationToken, Guid? locationId = null, IReadOnlyList<Guid>? reportLocations = null) =>
        LoadAsync(db, organizationId, contactType, null, toDate, cancellationToken, locationId, reportLocations);

    internal static string BalanceType(ContactType contactType, decimal signedBalance) =>
        contactType == ContactType.Customer
            ? (signedBalance >= 0 ? "DR" : "CR")
            : (signedBalance >= 0 ? "CR" : "DR");

    private static Task<List<Event>> LoadAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, Guid? contactId, DateOnly toDate,
        CancellationToken cancellationToken, Guid? locationId, IReadOnlyList<Guid>? reportLocations) =>
        contactType == ContactType.Customer
            ? LoadCustomerEventsAsync(db, organizationId, contactId, toDate, cancellationToken, locationId, reportLocations)
            : LoadSupplierEventsAsync(db, organizationId, contactId, toDate, cancellationToken, locationId, reportLocations);

    private static async Task<List<Event>> LoadCustomerEventsAsync(
        IAppDbContext db, Guid organizationId, Guid? contactId, DateOnly toDate, CancellationToken cancellationToken,
        Guid? locationId, IReadOnlyList<Guid>? reportLocations)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Status == InvoiceStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var invoiceTotals = invoiceLines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Status == CreditNoteStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var creditNoteTotals = creditNoteLines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Direction == PaymentDirection.Received && x.Status == PaymentStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.Amount, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        var paymentBaseAmounts = await SettlementBaseAmountsAsync(
            db, organizationId, DocumentType.Invoice,
            [.. payments.Select(x => (x.Id, x.Amount, x.ExchangeRate))], cancellationToken);

        var events = new List<Event>();
        events.AddRange(invoices.Select(x => new Event(
            x.ContactId, x.Date, DocumentType.Invoice, x.Code, x.Reference,
            ExchangeRates.ToBase(invoiceTotals.GetValueOrDefault(x.Id), x.ExchangeRate))));
        events.AddRange(creditNotes.Select(x => new Event(
            x.ContactId, x.Date, DocumentType.CreditNote, x.Code, x.Reference,
            -ExchangeRates.ToBase(creditNoteTotals.GetValueOrDefault(x.Id), x.ExchangeRate))));
        events.AddRange(payments.Select(x =>
            new Event(x.ContactId, x.Date, DocumentType.Payment, x.Code, x.Reference, -paymentBaseAmounts[x.Id])));
        events.AddRange(await LoadJournalVoucherEventsAsync(
            db, organizationId, ContactType.Customer, contactId, toDate, cancellationToken, locationId, reportLocations));
        return events;
    }

    private static async Task<List<Event>> LoadSupplierEventsAsync(
        IAppDbContext db, Guid organizationId, Guid? contactId, DateOnly toDate, CancellationToken cancellationToken,
        Guid? locationId, IReadOnlyList<Guid>? reportLocations)
    {
        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Status == PurchaseBillStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.TdsAmount, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillTotals = purchaseBillLines.GroupBy(x => x.PurchaseBillId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Status == ExpenseStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.SupplierInvoiceReference, x.TdsAmount, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var expenseTotals = expenseLines.GroupBy(x => x.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Status == DebitNoteStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.TdsAmount, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var debitNoteTotals = debitNoteLines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == organizationId && (contactId == null || x.ContactId == contactId)
                && x.Direction == PaymentDirection.Paid && x.Status == PaymentStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.Amount, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        var paymentBaseAmounts = await SettlementBaseAmountsAsync(
            db, organizationId, DocumentType.PurchaseBill,
            [.. payments.Select(x => (x.Id, x.Amount, x.ExchangeRate))], cancellationToken);

        var events = new List<Event>();
        events.AddRange(purchaseBills.Select(x => new Event(
            x.ContactId, x.Date, DocumentType.PurchaseBill, x.Code, x.Reference,
            ExchangeRates.ToBase(purchaseBillTotals.GetValueOrDefault(x.Id) - x.TdsAmount, x.ExchangeRate))));
        events.AddRange(expenses.Select(x => new Event(
            x.ContactId, x.Date, DocumentType.Expense, x.Code, x.SupplierInvoiceReference,
            ExchangeRates.ToBase(expenseTotals.GetValueOrDefault(x.Id) - x.TdsAmount, x.ExchangeRate))));
        events.AddRange(debitNotes.Select(x => new Event(
            x.ContactId, x.Date, DocumentType.DebitNote, x.Code, x.Reference,
            -ExchangeRates.ToBase(debitNoteTotals.GetValueOrDefault(x.Id) - x.TdsAmount, x.ExchangeRate))));
        events.AddRange(payments.Select(x =>
            new Event(x.ContactId, x.Date, DocumentType.Payment, x.Code, x.Reference, -paymentBaseAmounts[x.Id])));
        events.AddRange(await LoadJournalVoucherEventsAsync(
            db, organizationId, ContactType.Supplier, contactId, toDate, cancellationToken, locationId, reportLocations));
        return events;
    }

    /// <summary>
    /// Phase 36 -- each payment's value in the base currency, folded at <b>the rate of what it
    /// settles</b>: every allocation at its target document's own rate, and whatever remains
    /// unallocated at the payment's own rate.
    ///
    /// <para>That is what makes a fully settled invoice net to exactly zero in a contact's ledger
    /// even when the receipt came in at a different rate. Folding the payment at its own rate
    /// instead would leave the realised exchange difference sitting in the contact's balance as if
    /// the customer still owed it; that difference is booked to the forex account at Approve time
    /// (<c>PaymentForexCalculator</c>) and belongs there, not here.</para>
    ///
    /// <para>Cross-currency allocation is refused outright (phase 28 Decision F), so an
    /// allocation's Amount is always in both the payment's and the target's currency and the fold
    /// is unambiguous.</para>
    /// </summary>
    private static async Task<Dictionary<Guid, decimal>> SettlementBaseAmountsAsync(
        IAppDbContext db, Guid organizationId, DocumentType targetDocumentType,
        IReadOnlyList<(Guid Id, decimal Amount, decimal ExchangeRate)> payments, CancellationToken cancellationToken)
    {
        var result = payments.ToDictionary(x => x.Id, x => ExchangeRates.ToBase(x.Amount, x.ExchangeRate));

        if (payments.Count == 0 || payments.All(x => x.ExchangeRate == ExchangeRates.BaseRate))
        {
            // Every single-currency tenant, and most documents of a multi-currency one: nothing to
            // look up, and the answer is the payment's own amount.
            return result;
        }

        var paymentIds = payments.Select(x => x.Id).ToList();
        var allocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && paymentIds.Contains(x.SourceId)
                && x.TargetDocumentType == targetDocumentType)
            .Select(x => new { x.SourceId, x.TargetDocumentId, x.Amount })
            .ToListAsync(cancellationToken);

        if (allocations.Count == 0)
        {
            return result;
        }

        var targetIds = allocations.Select(x => x.TargetDocumentId).Distinct().ToList();
        var targetRates = targetDocumentType == DocumentType.Invoice
            ? await db.Invoices
                .Where(x => x.OrganizationId == organizationId && targetIds.Contains(x.Id))
                .Select(x => new { x.Id, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken)
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && targetIds.Contains(x.Id))
                .Select(x => new { x.Id, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken);

        foreach (var payment in payments)
        {
            var mine = allocations.Where(x => x.SourceId == payment.Id).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var allocatedBase = mine.Sum(x => ExchangeRates.ToBase(
                x.Amount,
                targetRates.TryGetValue(x.TargetDocumentId, out var rate) && rate != 0 ? rate : payment.ExchangeRate));

            var unallocated = payment.Amount - mine.Sum(x => x.Amount);
            result[payment.Id] = allocatedBase + ExchangeRates.ToBase(unallocated, payment.ExchangeRate);
        }

        return result;
    }

    /// <summary>
    /// Approved <c>JournalVoucherLine</c>s tagged with a contact, rolled up to <b>one event per
    /// (voucher, contact)</b> -- a voucher may carry several lines against the same contact, and
    /// the live report shows such a voucher once, not once per line.
    ///
    /// <para>Sign follows the side the contact's control account sits on: on the customer side a
    /// net <b>debit</b> increases what is owed to us, on the supplier side a net <b>credit</b>
    /// increases what we owe. That is the same asymmetry <see cref="BalanceType"/> encodes, applied
    /// to the movement rather than the balance.</para>
    /// </summary>
    private static async Task<List<Event>> LoadJournalVoucherEventsAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, Guid? contactId, DateOnly toDate,
        CancellationToken cancellationToken, Guid? locationId, IReadOnlyList<Guid>? reportLocations)
    {
        var vouchers = await db.JournalVouchers
            .Where(x => x.OrganizationId == organizationId
                && x.Status == JournalVoucherStatus.Approved && x.Date <= toDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        if (vouchers.Count == 0)
        {
            return [];
        }

        var voucherIds = vouchers.Select(x => x.Id).ToList();
        var lines = await db.JournalVoucherLines
            .Where(x => voucherIds.Contains(x.JournalVoucherId) && x.ContactId != null
                && (contactId == null || x.ContactId == contactId))
            .Select(x => new { x.JournalVoucherId, ContactId = x.ContactId!.Value, x.Debit, x.Credit })
            .ToListAsync(cancellationToken);

        // Only lines whose contact is of this report's own type: a Contact is either a Customer or
        // a Supplier here, and a customer-tagged line has no place in a supplier ledger.
        var taggedContactIds = lines.Select(x => x.ContactId).Distinct().ToList();
        var contactsOfType = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && x.Type == contactType && taggedContactIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var ofType = contactsOfType.ToHashSet();

        var voucherLookup = vouchers.ToDictionary(x => x.Id);

        return
        [
            .. lines
                .Where(x => ofType.Contains(x.ContactId))
                .GroupBy(x => new { x.JournalVoucherId, x.ContactId })
                .Select(g =>
                {
                    var voucher = voucherLookup[g.Key.JournalVoucherId];
                    var netDebit = g.Sum(x => x.Debit - x.Credit);
                    var signed = contactType == ContactType.Customer ? netDebit : -netDebit;
                    return new Event(
                        g.Key.ContactId, voucher.Date, DocumentType.JournalVoucher, voucher.Code, voucher.Reference,
                        ExchangeRates.ToBase(signed, voucher.ExchangeRate));
                })
                .Where(x => x.SignedAmount != 0),
        ];
    }
}
