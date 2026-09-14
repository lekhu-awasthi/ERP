using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.Ageing;

/// <summary>
/// The document types that can carry an outstanding balance against a contact, as the live Txn Type
/// filter enumerates them: Opening Balance, the trade document (Invoice on the customer side;
/// Purchase Bill and Expense on the supplier side), and Journal Voucher.
///
/// <para><b>The live filter offers one more option this codebase cannot express</b> -- "Quick
/// Payment" on the receivable side, "Quick Receipt" on the payable side. In the reference product
/// those are generic multi-line Accounts-table documents in their own right; here, phase-17
/// Decision #7 deliberately made Quick Payment/Receipt a thin variant of the existing
/// <c>Payment</c> aggregate rather than a new document type, so there is no such document to age.
/// An unallocated Payment is a credit against the contact, and it already reduces the contact's
/// balance through <c>ContactBalanceSummaryQuery</c>; it is not an outstanding item with an age.
/// Phase 36 re-examined this and kept it -- see docs/phase-36-status.md's decision on why an
/// unallocated receipt is not an ageable document even though the live filter lists one.</para>
///
/// <para><b>Opening Balance is a contact's own <c>Contact.OpeningBalance</c></b>, not an
/// <c>OpeningBalanceLine</c>: the latter is keyed by (OrganizationId, AccountId) and carries no
/// contact at all. It has no document number, no reference and no date, so it ages from the
/// as-of date itself -- age zero, status Current -- which is the honest rendering of a figure that
/// records a balance without recording when it arose.</para>
/// </summary>
public enum AgeableDocumentType
{
    OpeningBalance,
    Invoice,
    PurchaseBill,
    Expense,
    JournalVoucher,
}

/// <summary>One outstanding item: what was billed, what has been settled against it, and when it
/// fell due. <see cref="Balance"/> is the figure both ageing reports agree on.</summary>
internal sealed record OutstandingDocument(
    AgeableDocumentType Type,
    Guid Id,
    Guid ContactId,
    DateOnly Date,
    DateOnly DueDate,
    string Number,
    string? Reference,
    decimal Amount,
    decimal Paid,
    decimal ExchangeRate)
{
    public decimal Balance => Amount - Paid;

    /// <summary>The same row with every money field folded into the base currency at this
    /// document's own rate -- what both ageing reports publish (phase 36).</summary>
    public OutstandingDocument InBaseCurrency() =>
        this.ExchangeRate == ExchangeRates.BaseRate
            ? this
            : this with
            {
                Amount = ExchangeRates.ToBase(this.Amount, this.ExchangeRate),
                Paid = ExchangeRates.ToBase(this.Paid, this.ExchangeRate),
                ExchangeRate = ExchangeRates.BaseRate,
            };
}

/// <summary>
/// <b>The one place this codebase decides what a contact still owes, document by document.</b>
///
/// <para><b>Why it exists (phase 36).</b> Two reports answer that question -- phase 9's Ageing
/// Summary (buckets per contact) and phase 26b's Invoice Age / Purchase Bill Age (a row per
/// document) -- and they were two independent implementations of the same arithmetic. They had
/// already drifted twice: phase 26b counted Journal-Voucher-sourced allocations while phase 9's
/// counted only Payment-sourced ones, and phase 26b aged from the due date while phase 9's aged
/// from the document date. Phase 31 patched both divergences by editing the older handler to match
/// -- which left them agreeing by coincidence, not by construction, and still disagreeing about
/// <i>which documents are ageable at all</i>: the summary saw neither a contact-tagged Journal
/// Voucher nor a contact's own opening balance, both of which the per-document report has listed
/// since phase 26b.</para>
///
/// <para>Phase 26b's own lesson is the shape aimed for here: Invoice Age's total balance equals
/// Customer Receivable Summary's closing balance <i>by construction</i> because both read
/// <c>ContactLedgerReader</c>. After this, the two ageing reports agree the same way -- a bucket
/// total is a partition of exactly the rows the document report lists, and
/// <c>AgeingReportsAgreeTests</c> asserts it on data that exercises every candidate type.</para>
///
/// <para><b>The netting.</b> Outstanding is the document's net amount, less every Approved payment
/// allocation against it (from either source type -- a Payment, or a contact-tagged Journal Voucher
/// line, phase-17's polymorphic <c>PaymentAllocation.SourceType</c>), less every Approved linked
/// reversal (CreditNote for an Invoice, DebitNote for a PurchaseBill). Net amount is the gross for
/// an Invoice and gross-less-TDS for a PurchaseBill or Expense -- TDS is withheld from what is
/// actually payable to the supplier, mirroring <c>PurchaseBillPostingRule</c>'s own credit-AP-net-
/// of-TDS choice (phase 6).</para>
///
/// <para><b>Every figure is in the base currency</b> (phase 36). A document's amount, the
/// settlements against it and therefore its balance are all folded at <b>that document's own
/// rate</b> -- which is exact, because a cross-currency allocation is refused outright (phase 28
/// Decision F), so every settlement against a document is already in that document's currency. The
/// realised exchange difference a settlement produces is booked to the forex account at Approve
/// time (<c>PaymentForexCalculator</c>) and is deliberately not an ageing figure: an invoice
/// settled in full is not outstanding, whatever the rate moved to. A single-currency tenant is
/// untouched -- every rate is 1.</para>
///
/// <para><b>Due Date is real for Invoice, PurchaseBill and Expense</b> (phase 31 gave the first two
/// a stored one, seeded from the contact's Credit Term) and is the document's own date for the
/// rest, which is exactly how the live report renders its Journal Voucher rows.</para>
///
/// <para><b>Location, per phase 35b.</b> Both arguments narrow the <i>ageable documents only, never
/// the settlements against them</i>: a branch invoice settled by a payment raised at head office is
/// still settled, and filtering the settlement side too would show it outstanding on the branch's
/// ageing while the organization-wide report showed it paid. Allocations and reductions are keyed
/// to the already-filtered document ids, so they follow the narrowing without being narrowed. And
/// per phase 35b's own finding, this helper owns <b>every</b> condition the handlers' own
/// <c>Where</c> clauses carried -- the organization above all, since there is no global query
/// filter in this codebase.</para>
/// </summary>
internal static class OutstandingDocumentReader
{
    /// <summary>What the Number column shows for a contact's opening balance -- it is a figure on
    /// the Contact, not a numbered document, so there is nothing else to show.</summary>
    public const string OpeningBalanceLabel = "Opening Balance";

    /// <summary>
    /// Every item of <paramref name="contactType"/> with a non-zero balance as of
    /// <paramref name="asOfDate"/>. <paramref name="contactId"/> narrows to one contact;
    /// <paramref name="locationId"/> is the report's own Billing Location filter and
    /// <paramref name="reportLocations"/> the caller's permission scope.
    /// </summary>
    public static async Task<List<OutstandingDocument>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        ContactType contactType,
        DateOnly asOfDate,
        CancellationToken cancellationToken,
        Guid? contactId = null,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null)
    {
        var candidates = contactType == ContactType.Customer
            ? await LoadCustomerCandidatesAsync(db, organizationId, asOfDate, locationId, reportLocations, cancellationToken)
            : await LoadSupplierCandidatesAsync(db, organizationId, asOfDate, locationId, reportLocations, cancellationToken);

        candidates.AddRange(await LoadOpeningBalancesAsync(
            db, organizationId, contactType, asOfDate, contactId, cancellationToken));

        if (contactId is { } onlyContact)
        {
            candidates = [.. candidates.Where(x => x.ContactId == onlyContact)];
        }

        var reductions = contactType == ContactType.Customer
            ? await LoadCreditNoteReductionsAsync(db, organizationId, asOfDate, candidates, cancellationToken)
            : await LoadDebitNoteReductionsAsync(db, organizationId, asOfDate, candidates, cancellationToken);

        var allocations = await LoadAllocationsAsync(db, organizationId, contactType, candidates, cancellationToken);

        return
        [
            .. candidates
                .Select(x => x with { Paid = allocations.GetValueOrDefault(x.Id) + reductions.GetValueOrDefault(x.Id) })
                // Filter before the fold: a document settled in full is not outstanding at any
                // rate, and folding first could only turn an exact zero into a rounding artefact.
                .Where(x => x.Balance != 0)
                .Select(x => x.InBaseCurrency()),
        ];
    }

    /// <summary>A customer's opening balance is outstanding when it is a debit (positive); a
    /// supplier's when it is a credit. <c>Contact.OpeningBalance</c> is stored in the contact's own
    /// direction, so both cases read the same way: a positive figure is what this contact owes or
    /// is owed.</summary>
    private static async Task<List<OutstandingDocument>> LoadOpeningBalancesAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, DateOnly asOfDate, Guid? contactId,
        CancellationToken cancellationToken)
    {
        var contactsQuery = db.Contacts
            .Where(x => x.OrganizationId == organizationId && x.Type == contactType && x.OpeningBalance > 0);

        if (contactId is { } onlyContact)
        {
            contactsQuery = contactsQuery.Where(x => x.Id == onlyContact);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.OpeningBalance })
            .ToListAsync(cancellationToken);

        return contacts
            .Select(x => new OutstandingDocument(
                // Contact.OpeningBalance is already a base-currency figure -- a contact has no
                // currency of its own -- so it folds at rate 1.
                AgeableDocumentType.OpeningBalance, x.Id, x.Id, asOfDate, asOfDate, OpeningBalanceLabel, null,
                Math.Abs(x.OpeningBalance), 0m, ExchangeRates.BaseRate))
            .ToList();
    }

    private static async Task<List<OutstandingDocument>> LoadCustomerCandidatesAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken)
    {
        var invoiceQuery = db.Invoices
            .Where(x => x.OrganizationId == organizationId
                && x.Status == InvoiceStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        var invoices = await invoiceQuery
            .Select(x => new { x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        // Phase 42 -- the line totals are summed in SQL against the same invoice *query*, not
        // fetched for a materialised list of ids. The id list was every approved invoice up to the
        // as-of date: 50,000 of them on the scale dataset, handed back as an OPENJSON parameter and
        // joined to InvoiceLines, which is phase-34c's gotcha at full size. Joining the query
        // instead leaves the filter where the optimizer can see it, and a GroupBy that runs
        // store-side returns one small row per invoice rather than every line.
        var totals = await (
            from line in db.InvoiceLines
            join invoice in invoiceQuery on line.InvoiceId equals invoice.Id
            group line by line.InvoiceId into g
            select new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, cancellationToken);

        var candidates = invoices
            .Select(x => new OutstandingDocument(
                AgeableDocumentType.Invoice, x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference,
                totals.GetValueOrDefault(x.Id), 0m, x.ExchangeRate))
            .ToList();

        candidates.AddRange(await LoadJournalVoucherCandidatesAsync(
            db, organizationId, ContactType.Customer, asOfDate, locationId, reportLocations, cancellationToken));
        return candidates;
    }

    private static async Task<List<OutstandingDocument>> LoadSupplierCandidatesAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken)
    {
        var billQuery = db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId
                && x.Status == PurchaseBillStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        var bills = await billQuery
            .Select(x => new { x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference, x.TdsAmount, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        // Phase 42 -- see the invoice half above: the totals are a store-side GroupBy over the same
        // query, not a fetch keyed by a materialised id list.
        var billTotals = await (
            from line in db.PurchaseBillLines
            join bill in billQuery on line.PurchaseBillId equals bill.Id
            group line by line.PurchaseBillId into g
            select new { BillId = g.Key, Total = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.BillId, x => x.Total, cancellationToken);

        var expenseQuery = db.Expenses
            .Where(x => x.OrganizationId == organizationId
                && x.Status == ExpenseStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        var expenses = await expenseQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.SupplierInvoiceReference, x.TdsAmount, x.ExchangeRate,
            })
            .ToListAsync(cancellationToken);

        var expenseTotals = await (
            from line in db.ExpenseLines
            join expense in expenseQuery on line.ExpenseId equals expense.Id
            group line by line.ExpenseId into g
            select new { ExpenseId = g.Key, Total = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.ExpenseId, x => x.Total, cancellationToken);

        var candidates = bills
            .Select(x => new OutstandingDocument(
                AgeableDocumentType.PurchaseBill, x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference,
                billTotals.GetValueOrDefault(x.Id) - x.TdsAmount, 0m, x.ExchangeRate))
            .ToList();

        // Expense stored a real due date from phase 6; Invoice and PurchaseBill joined it in 31.
        candidates.AddRange(expenses.Select(x => new OutstandingDocument(
            AgeableDocumentType.Expense, x.Id, x.ContactId, x.Date, x.DueDate ?? x.Date, x.Code,
            x.SupplierInvoiceReference, expenseTotals.GetValueOrDefault(x.Id) - x.TdsAmount, 0m, x.ExchangeRate)));

        candidates.AddRange(await LoadJournalVoucherCandidatesAsync(
            db, organizationId, ContactType.Supplier, asOfDate, locationId, reportLocations, cancellationToken));
        return candidates;
    }

    /// <summary>
    /// A contact-tagged Journal Voucher is outstanding when its net movement runs the same way the
    /// contact's balance does -- a net debit on the customer side, a net credit on the supplier
    /// side. A voucher that moves the balance the other way is a credit, not an ageable item, and
    /// is left to <c>ContactBalanceSummaryQuery</c>.
    ///
    /// <para>The candidate's Id is the <b>voucher's</b> id, not the line's: the live report shows
    /// one row per voucher.</para>
    /// </summary>
    private static async Task<List<OutstandingDocument>> LoadJournalVoucherCandidatesAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, DateOnly asOfDate, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken)
    {
        var vouchers = await db.JournalVouchers
            .Where(x => x.OrganizationId == organizationId
                && x.Status == JournalVoucherStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        if (vouchers.Count == 0)
        {
            return [];
        }

        var voucherIds = vouchers.Select(x => x.Id).ToList();
        var lines = await db.JournalVoucherLines
            .Where(x => voucherIds.Contains(x.JournalVoucherId) && x.ContactId != null)
            .Select(x => new { x.JournalVoucherId, ContactId = x.ContactId!.Value, x.Debit, x.Credit })
            .ToListAsync(cancellationToken);

        // Only lines tagged with a contact of this report's own type -- a supplier-tagged line can
        // never appear in a customer ageing (ContactLedgerReader makes the same check).
        var contactTypes = await db.Contacts
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var voucherLookup = vouchers.ToDictionary(x => x.Id);

        return
        [
            .. lines
                .Where(x => contactTypes.GetValueOrDefault(x.ContactId) == contactType)
                .GroupBy(x => new { x.JournalVoucherId, x.ContactId })
                .Select(g =>
                {
                    var voucher = voucherLookup[g.Key.JournalVoucherId];
                    var netDebit = g.Sum(x => x.Debit - x.Credit);
                    var outstanding = contactType == ContactType.Customer ? netDebit : -netDebit;
                    return new OutstandingDocument(
                        AgeableDocumentType.JournalVoucher, g.Key.JournalVoucherId, g.Key.ContactId,
                        voucher.Date, voucher.Date, voucher.Code, voucher.Reference, outstanding, 0m,
                        voucher.ExchangeRate);
                })
                .Where(x => x.Amount > 0),
        ];
    }

    private static async Task<Dictionary<Guid, decimal>> LoadAllocationsAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, List<OutstandingDocument> candidates,
        CancellationToken cancellationToken)
    {
        var targetType = contactType == ContactType.Customer ? DocumentType.Invoice : DocumentType.PurchaseBill;
        var paymentDirection = contactType == ContactType.Customer ? PaymentDirection.Received : PaymentDirection.Paid;

        var hasTargets = candidates.Any(x => x.Type is AgeableDocumentType.Invoice or AgeableDocumentType.PurchaseBill);

        if (!hasTargets)
        {
            return [];
        }

        // Phase 42 -- the two queries below used to be narrowed by the ids of those candidates.
        // On the scale dataset that is 50,000 GUIDs, serialised into an nvarchar(max) JSON
        // parameter of about 1.8 MB and joined back through OPENJSON: measured at 633 ms against
        // a table holding almost no rows, which is the cost of the *parameter*, not of the query.
        //
        // The narrowing was never load-bearing. Both queries are already scoped to this
        // organization through their join -- a Payment's or a JournalVoucher's own OrganizationId
        // -- and to this side of the ledger through targetType, and the dictionary they build is
        // read with GetValueOrDefault against the candidates, so an allocation against a document
        // that is not a candidate lands in the dictionary and is never looked up. Same figures,
        // no parameter.

        // Two queries rather than one union: a Payment is validated by its Direction and a voucher
        // by its own Status, and the joins differ.
        var fromPayments = await (
                from a in db.PaymentAllocations
                where a.SourceType == DocumentType.Payment
                join p in db.Payments on a.SourceId equals p.Id
                where a.TargetDocumentType == targetType
                      && p.OrganizationId == organizationId
                      && p.Direction == paymentDirection && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { TargetId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var fromVouchers = await (
                from a in db.PaymentAllocations
                where a.SourceType == DocumentType.JournalVoucher
                join l in db.JournalVoucherLines on a.SourceId equals l.Id
                join v in db.JournalVouchers on l.JournalVoucherId equals v.Id
                where a.TargetDocumentType == targetType
                      && v.OrganizationId == organizationId && v.Status == JournalVoucherStatus.Approved
                group a by a.TargetDocumentId into g
                select new { TargetId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var row in fromPayments.Concat(fromVouchers))
        {
            result[row.TargetId] = result.GetValueOrDefault(row.TargetId) + row.Allocated;
        }

        return result;
    }

    private static async Task<Dictionary<Guid, decimal>> LoadCreditNoteReductionsAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, List<OutstandingDocument> candidates,
        CancellationToken cancellationToken)
    {
        if (!candidates.Any(x => x.Type == AgeableDocumentType.Invoice))
        {
            return [];
        }

        // Phase 42 -- scoped by the tenant and the period, not by the ids of the candidate
        // documents. The reductions are read back with GetValueOrDefault against the candidates,
        // so a note against a document outside that set is fetched and never looked up; what the
        // id list bought was a smaller result at the price of a parameter as long as the period.
        // See LoadAllocationsAsync for the measurement.
        var creditNoteQuery = db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == CreditNoteStatus.Approved
                && x.Date <= asOfDate && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId != null);

        var creditNotes = await creditNoteQuery
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value })
            .ToListAsync(cancellationToken);

        var gross = await (
            from line in db.CreditNoteLines
            join note in creditNoteQuery on line.CreditNoteId equals note.Id
            group line by line.CreditNoteId into g
            select new { CreditNoteId = g.Key, Total = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.CreditNoteId, x => x.Total, cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var note in creditNotes)
        {
            result[note.ReferrerId] = result.GetValueOrDefault(note.ReferrerId) + gross.GetValueOrDefault(note.Id);
        }

        return result;
    }

    private static async Task<Dictionary<Guid, decimal>> LoadDebitNoteReductionsAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, List<OutstandingDocument> candidates,
        CancellationToken cancellationToken)
    {
        if (!candidates.Any(x => x.Type == AgeableDocumentType.PurchaseBill))
        {
            return [];
        }

        // Phase 42 -- see LoadCreditNoteReductionsAsync; this is the supplier-side twin.
        var debitNoteQuery = db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == DebitNoteStatus.Approved
                && x.Date <= asOfDate && x.ReferrerType == DocumentType.PurchaseBill
                && x.ReferrerId != null);

        var debitNotes = await debitNoteQuery
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);

        var gross = await (
            from line in db.DebitNoteLines
            join note in debitNoteQuery on line.DebitNoteId equals note.Id
            group line by line.DebitNoteId into g
            select new { DebitNoteId = g.Key, Total = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.DebitNoteId, x => x.Total, cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var note in debitNotes)
        {
            var net = gross.GetValueOrDefault(note.Id) - note.TdsAmount;
            result[note.ReferrerId] = result.GetValueOrDefault(note.ReferrerId) + net;
        }

        return result;
    }
}
