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
    /// <paramref name="reportLocations"/> the caller's permission scope. Phase 91 -- <paramref name="invoiceId"/> narrows
    /// a customer's to one invoice (see <see cref="LoadInDocumentCurrencyAsync"/>).
    /// </summary>
    public static async Task<List<OutstandingDocument>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        ContactType contactType,
        DateOnly asOfDate,
        CancellationToken cancellationToken,
        Guid? contactId = null,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null,
        Guid? invoiceId = null) =>
        [
            .. (await LoadInDocumentCurrencyAsync(
                    db, organizationId, contactType, asOfDate, cancellationToken, contactId, locationId, reportLocations,
                    invoiceId))
                .Select(x => x.InBaseCurrency()),
        ];

    /// <summary>
    /// Phase 88 -- the same rows as <see cref="LoadAsync"/>, before the fold: every money field in the document's own
    /// currency. What a payment may still allocate to a document is this figure, because an allocation is in the
    /// payment's currency and a payment only settles documents in its own (phase 28 Decision F).
    ///
    /// <para><b>Phase 91 -- <paramref name="invoiceId"/>: one invoice, read through itself.</b> A till refund asks what
    /// one sale is still owed, and that sale's customer is the walk-in, who owns every counter sale: narrowing by contact
    /// read all of them. Measured on the scale tenant with 20,000 walk-in sales and 2,000 refunds, the refund screen's
    /// read of one sale cost 56,566 logical reads and 1.3 s of CPU (tools/scale/probe-phase91-*.csv). Narrowed to the
    /// invoice, the same pipeline reads that invoice's lines, tenders, allocations and notes and nothing else, so the
    /// answer is the row the contact-wide read would have produced for it, by construction rather than by a second
    /// calculation. Opening balances and journal vouchers are no invoice, so they are not read. Customers only.</para>
    /// </summary>
    public static async Task<List<OutstandingDocument>> LoadInDocumentCurrencyAsync(
        IAppDbContext db,
        Guid organizationId,
        ContactType contactType,
        DateOnly asOfDate,
        CancellationToken cancellationToken,
        Guid? contactId = null,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null,
        Guid? invoiceId = null)
    {
        if (invoiceId is not null && contactType != ContactType.Customer)
        {
            throw new ArgumentException("Only a customer's documents can be narrowed to one invoice.", nameof(invoiceId));
        }

        var candidates = contactType == ContactType.Customer
            ? await LoadCustomerCandidatesAsync(
                db, organizationId, asOfDate, contactId, locationId, reportLocations, cancellationToken, invoiceId)
            : await LoadSupplierCandidatesAsync(
                db, organizationId, asOfDate, contactId, locationId, reportLocations, cancellationToken);

        if (invoiceId is null)
        {
            candidates.AddRange(await LoadOpeningBalancesAsync(
                db, organizationId, contactType, asOfDate, contactId, cancellationToken));
        }

        // Phase 90 -- one contact's settlements are read through that contact's documents, as a subquery, never as a
        // list of ids. Organization-wide, the settlement queries read every allocation, tender and note the
        // organization has; measured on the 50,001-invoice tenant with 30,000 allocations and 20,000 tenders, one
        // customer's picker read 38,015 logical reads for documents worth about 30 (tools/scale/probe-phase90-*.csv).
        // Null for the organization-wide callers, which phase 42 measured and left scoped by the organization alone.
        IQueryable<Guid>? targetIds = null;
        if (contactId is { } onlyContact)
        {
            candidates = [.. candidates.Where(x => x.ContactId == onlyContact)];
            targetIds = contactType == ContactType.Customer
                ? db.Invoices.Where(x => x.OrganizationId == organizationId && x.ContactId == onlyContact).Select(x => x.Id)
                : db.PurchaseBills.Where(x => x.OrganizationId == organizationId && x.ContactId == onlyContact).Select(x => x.Id);
        }

        // Phase 91 -- one invoice: its settlements through itself, the same subquery shape as one contact's.
        if (invoiceId is { } onlyInvoice)
        {
            targetIds = db.Invoices.Where(x => x.OrganizationId == organizationId && x.Id == onlyInvoice).Select(x => x.Id);
        }

        var reductions = contactType == ContactType.Customer
            ? await LoadCreditNoteReductionsAsync(db, organizationId, asOfDate, candidates, targetIds, cancellationToken)
            : await LoadDebitNoteReductionsAsync(db, organizationId, asOfDate, candidates, targetIds, cancellationToken);

        var allocations = await LoadAllocationsAsync(
            db, organizationId, contactType, candidates, contactId, targetIds, cancellationToken, invoiceId);

        return
        [
            .. candidates
                .Select(x => x with { Paid = allocations.GetValueOrDefault(x.Id) + reductions.GetValueOrDefault(x.Id) })
                // Filter before the fold: a document settled in full is not outstanding at any
                // rate, and folding first could only turn an exact zero into a rounding artefact.
                .Where(x => x.Balance != 0),
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
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, Guid? contactId, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken, Guid? invoiceId = null)
    {
        var invoiceQuery = db.Invoices
            .Where(x => x.OrganizationId == organizationId
                && x.Status == InvoiceStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        // Phase 88 -- one contact's documents are narrowed here, in SQL, not after loading the organization's: the
        // payment forms ask this per contact, and a 50,000-invoice tenant would otherwise load every invoice to list a
        // handful. A second Where, never `contactId == null || ...` (phase 33).
        if (contactId is { } onlyContact)
        {
            invoiceQuery = invoiceQuery.Where(x => x.ContactId == onlyContact);
        }

        // Phase 91 -- one invoice, the same composed Where.
        if (invoiceId is { } onlyInvoice)
        {
            invoiceQuery = invoiceQuery.Where(x => x.Id == onlyInvoice);
        }

        var invoices = await invoiceQuery
            .Select(x => new { x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference, x.ExchangeRate, x.RoundOff })
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
            // Phase 61 -- a till line's service charge is part of what is owed, and so is the bill's
            // round-off (added below). Both are zero on an ERP invoice.
            select new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, cancellationToken);

        var candidates = invoices
            .Select(x => new OutstandingDocument(
                AgeableDocumentType.Invoice, x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.Reference,
                totals.GetValueOrDefault(x.Id) + x.RoundOff, 0m, x.ExchangeRate))
            .ToList();

        // A journal voucher's line is no invoice, so one invoice's read has none.
        if (invoiceId is null)
        {
            candidates.AddRange(await LoadJournalVoucherCandidatesAsync(
                db, organizationId, ContactType.Customer, asOfDate, contactId, locationId, reportLocations, cancellationToken));
        }

        return candidates;
    }

    private static async Task<List<OutstandingDocument>> LoadSupplierCandidatesAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, Guid? contactId, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken)
    {
        var billQuery = db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId
                && x.Status == PurchaseBillStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        if (contactId is { } onlyContact)
        {
            billQuery = billQuery.Where(x => x.ContactId == onlyContact);
        }

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

        if (contactId is { } expenseContact)
        {
            expenseQuery = expenseQuery.Where(x => x.ContactId == expenseContact);
        }

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
            db, organizationId, ContactType.Supplier, asOfDate, contactId, locationId, reportLocations, cancellationToken));
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
        IAppDbContext db, Guid organizationId, ContactType contactType, DateOnly asOfDate, Guid? contactId, Guid? locationId,
        IReadOnlyList<Guid>? reportLocations, CancellationToken cancellationToken)
    {
        var voucherQuery = db.JournalVouchers
            .Where(x => x.OrganizationId == organizationId
                && x.Status == JournalVoucherStatus.Approved && x.Date <= asOfDate)
            .AtLocations(locationId, reportLocations);

        // Phase 90 -- one query joining the vouchers, their contact-tagged lines and the contact, where there were three:
        // every approved voucher, then their lines by a list of those ids (an OPENJSON parameter as long as the
        // organization's vouchers, phase-34c's gotcha), then every contact's type (50,003 rows on the scale tenant) to
        // keep the lines of this report's own type. A supplier-tagged line still never appears in a customer ageing
        // (ContactLedgerReader makes the same check); the join to the contact is that check, made in SQL.
        var lineQuery =
            from line in db.JournalVoucherLines
            join voucher in voucherQuery on line.JournalVoucherId equals voucher.Id
            join contact in db.Contacts on line.ContactId equals (Guid?)contact.Id
            where contact.OrganizationId == organizationId && contact.Type == contactType
            select new
            {
                line.JournalVoucherId, ContactId = contact.Id, line.Debit, line.Credit,
                voucher.Date, voucher.Code, voucher.Reference, voucher.ExchangeRate,
            };
        if (contactId is { } onlyContact)
        {
            lineQuery = lineQuery.Where(x => x.ContactId == onlyContact);
        }

        var lines = await lineQuery.ToListAsync(cancellationToken);

        return
        [
            .. lines
                .GroupBy(x => new { x.JournalVoucherId, x.ContactId })
                .Select(g =>
                {
                    var voucher = g.First();
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
        Guid? contactId, IQueryable<Guid>? targetIds, CancellationToken cancellationToken, Guid? invoiceId = null)
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

        // Phase 90 -- that reasoning is about the organization-wide callers. One contact's documents are a subquery
        // (targetIds), not a list: SQL Server seeks the target index per document instead of reading the
        // organization's allocations, and no parameter grows with anything.
        var allocations = db.PaymentAllocations.Where(a => a.TargetDocumentType == targetType);
        if (targetIds is not null)
        {
            allocations = allocations.Where(a => targetIds.Contains(a.TargetDocumentId));
        }

        // Two queries rather than one union: a Payment is validated by its Direction and a voucher
        // by its own Status, and the joins differ.
        var fromPayments = await (
                from a in allocations
                where a.SourceType == DocumentType.Payment
                join p in db.Payments on a.SourceId equals p.Id
                where p.OrganizationId == organizationId
                      && p.Direction == paymentDirection && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { TargetId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var fromVouchers = await (
                from a in allocations
                where a.SourceType == DocumentType.JournalVoucher
                join l in db.JournalVoucherLines on a.SourceId equals l.Id
                join v in db.JournalVouchers on l.JournalVoucherId equals v.Id
                where v.OrganizationId == organizationId && v.Status == JournalVoucherStatus.Approved
                group a by a.TargetDocumentId into g
                select new { TargetId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var row in fromPayments.Concat(fromVouchers))
        {
            result[row.TargetId] = result.GetValueOrDefault(row.TargetId) + row.Allocated;
        }

        if (contactType == ContactType.Customer)
        {
            foreach (var (settledId, settled) in await LoadTenderSettlementsAsync(
                         db, organizationId, cancellationToken, contactId, invoiceId))
            {
                result[settledId] = result.GetValueOrDefault(settledId) + settled;
            }
        }

        return result;
    }

    /// <summary>
    /// Phase 61 (phase 59 Decision D) -- <b>a till sale's tenders are allocations against itself</b>:
    /// what each Approved invoice's tenders settled, handed over less change given back. This is what
    /// makes a fully paid counter sale not outstanding, and a part-paid one outstanding by exactly
    /// the part left on credit -- the same figure the sale's two GL entries leave on the receivable,
    /// so ageing, Customer Receivable Summary and the ledger agree by construction rather than by a
    /// second calculation. Scoped like the two queries above: by organization through the invoice,
    /// never by a materialised id list (phase 42), and by <paramref name="contactId"/> through the
    /// invoice too when one contact is asked about (phase 90), and by <paramref name="invoiceId"/> when one invoice is
    /// (phase 91).
    /// </summary>
    internal static async Task<Dictionary<Guid, decimal>> LoadTenderSettlementsAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken, Guid? contactId = null,
        Guid? invoiceId = null)
    {
        var invoices = db.Invoices.Where(x => x.OrganizationId == organizationId && x.Status == InvoiceStatus.Approved);
        if (contactId is { } onlyContact)
        {
            invoices = invoices.Where(x => x.ContactId == onlyContact);
        }

        if (invoiceId is { } onlyInvoice)
        {
            invoices = invoices.Where(x => x.Id == onlyInvoice);
        }

        var tendered = await (
                from t in db.InvoiceTenders
                join i in invoices on t.InvoiceId equals i.Id
                group t by t.InvoiceId into g
                select new { InvoiceId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Amount, cancellationToken);

        if (tendered.Count == 0)
        {
            return tendered;
        }

        var change = await invoices
            .Where(x => x.ChangeAmount > 0)
            .Select(x => new { x.Id, x.ChangeAmount })
            .ToListAsync(cancellationToken);

        foreach (var row in change)
        {
            tendered[row.Id] = tendered.GetValueOrDefault(row.Id) - row.ChangeAmount;
        }

        return tendered;
    }

    private static async Task<Dictionary<Guid, decimal>> LoadCreditNoteReductionsAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, List<OutstandingDocument> candidates,
        IQueryable<Guid>? targetIds, CancellationToken cancellationToken)
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
        // Phase 90 -- one contact: the notes against that contact's invoices (see LoadAllocationsAsync).
        if (targetIds is not null)
        {
            creditNoteQuery = creditNoteQuery.Where(x => targetIds.Contains(x.ReferrerId!.Value));
        }

        var creditNotes = await creditNoteQuery
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value, x.RoundOff })
            .ToListAsync(cancellationToken);

        // Phase 63 -- a till refund's service charge and round-off are part of what it gives back
        // (both zero on an ERP note).
        var gross = await (
            from line in db.CreditNoteLines
            join note in creditNoteQuery on line.CreditNoteId equals note.Id
            group line by line.CreditNoteId into g
            select new { CreditNoteId = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.CreditNoteId, x => x.Total, cancellationToken);

        var paidOut = await LoadRefundPayoutsAsync(db, creditNoteQuery, cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var note in creditNotes)
        {
            var reduction = gross.GetValueOrDefault(note.Id) + note.RoundOff - paidOut.GetValueOrDefault(note.Id);
            result[note.ReferrerId] = result.GetValueOrDefault(note.ReferrerId) + reduction;
        }

        return result;
    }

    /// <summary>
    /// Phase 63 -- <b>a till refund's payouts are the credit note paying itself out</b>, the mirror of
    /// <see cref="LoadTenderSettlementsAsync"/>: what was handed back over the counter never reaches the
    /// sale's balance, so only the rest of the note -- the part left on the customer's account -- reduces
    /// what the sale is owed. Without it, a cash refund of a paid sale would leave the sale owed a
    /// negative amount, the walk-in's balance would fall with every refund, and the payout entry's
    /// debit to the receivable would have nothing in any report to answer it.
    /// </summary>
    internal static async Task<Dictionary<Guid, decimal>> LoadRefundPayoutsAsync(
        IAppDbContext db, IQueryable<CreditNote> creditNotes, CancellationToken cancellationToken) =>
        await (
                from p in db.CreditNotePayouts
                join n in creditNotes on p.CreditNoteId equals n.Id
                group p by p.CreditNoteId into g
                select new { CreditNoteId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.CreditNoteId, x => x.Amount, cancellationToken);

    private static async Task<Dictionary<Guid, decimal>> LoadDebitNoteReductionsAsync(
        IAppDbContext db, Guid organizationId, DateOnly asOfDate, List<OutstandingDocument> candidates,
        IQueryable<Guid>? targetIds, CancellationToken cancellationToken)
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
        if (targetIds is not null)
        {
            debitNoteQuery = debitNoteQuery.Where(x => targetIds.Contains(x.ReferrerId!.Value));
        }

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
