using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.DocumentAge;

/// <summary>
/// Per-document outstanding, aged. Outstanding is the document's net amount less every Approved
/// payment allocation against it and every Approved linked reversal (CreditNote for Invoice,
/// DebitNote for PurchaseBill) -- the same netting <c>ContactAgeingSummaryQueryHandler</c> does,
/// reported per document here instead of rolled into buckets.
///
/// <para><b>Due Date is the document's own date wherever this codebase stores no due date.</b>
/// Only <c>Expense</c> carries a <c>DueDate</c> column; Invoice and PurchaseBill do not, and no
/// Contact carries a credit term to derive one from -- the same gap phase-9 recorded when it
/// dropped the live Ageing Summary's "Credit Term" column. So the Due Date column is real where
/// the data is real and equal to the document date everywhere else, which is exactly how the live
/// report renders its own Journal Voucher and quick-document rows. A stored due date on Invoice
/// and PurchaseBill belongs with Credit Terms as a whole and is named as a follow-up in
/// docs/phase-26b-status.md rather than half-built here.</para>
///
/// <para><b>Allocations sourced from a Journal Voucher count.</b> Since phase-17
/// <c>PaymentAllocation.SourceType</c> is polymorphic, and a contact-tagged JV line can pay an
/// invoice down. <c>ContactAgeingSummaryQueryHandler</c> still counts only Payment-sourced
/// allocations -- a limitation phase-17 flagged and this report does not inherit; see
/// docs/phase-26b-status.md's Decision B.</para>
/// </summary>
public sealed class DocumentAgeQueryHandler(IAppDbContext db)
    : IRequestHandler<DocumentAgeQuery, DocumentAgeDto>
{
    private sealed record Candidate(
        AgeableDocumentType Type,
        Guid Id,
        Guid ContactId,
        DateOnly Date,
        DateOnly DueDate,
        string Number,
        string? Reference,
        decimal Amount);

    public async Task<DocumentAgeDto> Handle(DocumentAgeQuery request, CancellationToken cancellationToken)
    {
        var contactsQuery = db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && x.Type == request.ContactType);

        if (request.ContactId is { } onlyContact)
        {
            contactsQuery = contactsQuery.Where(x => x.Id == onlyContact);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId, x.OpeningBalance })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var candidates = request.ContactType == ContactType.Customer
            ? await LoadCustomerCandidatesAsync(request, cancellationToken)
            : await LoadSupplierCandidatesAsync(request, cancellationToken);

        candidates.AddRange(contacts.Values
            .Where(x => OpeningBalanceIsOutstanding(request.ContactType, x.OpeningBalance))
            .Select(x => new Candidate(
                AgeableDocumentType.OpeningBalance,
                x.Id,
                x.Id,
                request.AsOfDate,
                request.AsOfDate,
                OpeningBalanceLabel,
                null,
                Math.Abs(x.OpeningBalance))));

        var wanted = request.DocumentTypes is { Count: > 0 } ? request.DocumentTypes.ToHashSet() : null;
        if (wanted is not null)
        {
            candidates = [.. candidates.Where(x => wanted.Contains(x.Type))];
        }

        var reductions = request.ContactType == ContactType.Customer
            ? await LoadCreditNoteReductionsAsync(request, candidates, cancellationToken)
            : await LoadDebitNoteReductionsAsync(request, candidates, cancellationToken);

        var allocations = await LoadAllocationsAsync(request, candidates, cancellationToken);

        var rows = new List<DocumentAgeRowDto>();

        foreach (var candidate in candidates)
        {
            if (!contacts.TryGetValue(candidate.ContactId, out var contact))
            {
                continue; // filtered out by ContactId, or not a contact of this report's type
            }

            var paid = allocations.GetValueOrDefault(candidate.Id) + reductions.GetValueOrDefault(candidate.Id);
            var balance = candidate.Amount - paid;

            if (balance == 0)
            {
                continue;
            }

            var overdueBy = request.AsOfDate.DayNumber - candidate.DueDate.DayNumber;

            rows.Add(new DocumentAgeRowDto(
                candidate.Type,
                candidate.Id,
                candidate.Date,
                candidate.DueDate,
                candidate.Number,
                candidate.Reference,
                contact.Id,
                contact.Code,
                contact.Name,
                contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null,
                candidate.Amount,
                paid,
                balance,
                overdueBy > 0 ? DocumentAgeRowDto.Overdue : DocumentAgeRowDto.Current,
                Math.Max(0, overdueBy)));
        }

        // Oldest due first -- the order an ageing report is read in, and the live screen's own.
        var ordered = rows.OrderBy(x => x.DueDate).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        return new DocumentAgeDto(
            request.ContactType,
            request.FromDate,
            request.AsOfDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            ordered.Sum(x => x.Amount),
            ordered.Sum(x => x.Paid),
            ordered.Sum(x => x.Balance));
    }

    /// <summary>What the Number column shows for a contact's opening balance -- it is a figure on
    /// the Contact, not a numbered document, so there is nothing else to show.</summary>
    private const string OpeningBalanceLabel = "Opening Balance";

    /// <summary>A customer's opening balance is outstanding when it is a debit (positive); a
    /// supplier's when it is a credit. <c>Contact.OpeningBalance</c> is stored in the contact's own
    /// direction, so both cases read the same way: a positive figure is what this contact owes or
    /// is owed.</summary>
    private static bool OpeningBalanceIsOutstanding(ContactType contactType, decimal openingBalance) =>
        openingBalance > 0;

    private async Task<List<Candidate>> LoadCustomerCandidatesAsync(DocumentAgeQuery request, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == InvoiceStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference })
            .ToListAsync(cancellationToken);

        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var lines = await db.InvoiceLines
            .Where(x => invoiceIds.Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var totals = lines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var candidates = invoices
            .Select(x => new Candidate(
                AgeableDocumentType.Invoice, x.Id, x.ContactId, x.Date, x.Date, x.Code, x.Reference,
                totals.GetValueOrDefault(x.Id)))
            .ToList();

        candidates.AddRange(await LoadJournalVoucherCandidatesAsync(request, cancellationToken));
        return candidates;
    }

    private async Task<List<Candidate>> LoadSupplierCandidatesAsync(DocumentAgeQuery request, CancellationToken cancellationToken)
    {
        var bills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == PurchaseBillStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.Code, x.Reference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var billIds = bills.Select(x => x.Id).ToList();
        var billLines = await db.PurchaseBillLines
            .Where(x => billIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var billTotals = billLines.GroupBy(x => x.PurchaseBillId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == ExpenseStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.DueDate, x.Code, x.SupplierInvoiceReference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var expenseIds = expenses.Select(x => x.Id).ToList();
        var expenseLines = await db.ExpenseLines
            .Where(x => expenseIds.Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var expenseTotals = expenseLines.GroupBy(x => x.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var candidates = bills
            .Select(x => new Candidate(
                AgeableDocumentType.PurchaseBill, x.Id, x.ContactId, x.Date, x.Date, x.Code, x.Reference,
                billTotals.GetValueOrDefault(x.Id) - x.TdsAmount))
            .ToList();

        // Expense is the one document type in this codebase that stores a real due date.
        candidates.AddRange(expenses.Select(x => new Candidate(
            AgeableDocumentType.Expense, x.Id, x.ContactId, x.Date, x.DueDate ?? x.Date, x.Code, x.SupplierInvoiceReference,
            expenseTotals.GetValueOrDefault(x.Id) - x.TdsAmount)));

        candidates.AddRange(await LoadJournalVoucherCandidatesAsync(request, cancellationToken));
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
    private async Task<List<Candidate>> LoadJournalVoucherCandidatesAsync(DocumentAgeQuery request, CancellationToken cancellationToken)
    {
        var vouchers = await db.JournalVouchers
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == JournalVoucherStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference })
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

        var voucherLookup = vouchers.ToDictionary(x => x.Id);

        return
        [
            .. lines
                .GroupBy(x => new { x.JournalVoucherId, x.ContactId })
                .Select(g =>
                {
                    var voucher = voucherLookup[g.Key.JournalVoucherId];
                    var netDebit = g.Sum(x => x.Debit - x.Credit);
                    var outstanding = request.ContactType == ContactType.Customer ? netDebit : -netDebit;
                    return new Candidate(
                        AgeableDocumentType.JournalVoucher, g.Key.JournalVoucherId, g.Key.ContactId,
                        voucher.Date, voucher.Date, voucher.Code, voucher.Reference, outstanding);
                })
                .Where(x => x.Amount > 0),
        ];
    }

    /// <summary>
    /// Approved allocations against the trade documents in scope, from either source type -- a
    /// Payment, or a contact-tagged Journal Voucher line (phase-17's polymorphic
    /// <c>PaymentAllocation.SourceType</c>).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadAllocationsAsync(
        DocumentAgeQuery request, List<Candidate> candidates, CancellationToken cancellationToken)
    {
        var targetType = request.ContactType == ContactType.Customer ? DocumentType.Invoice : DocumentType.PurchaseBill;
        var paymentDirection = request.ContactType == ContactType.Customer ? PaymentDirection.Received : PaymentDirection.Paid;

        var targetIds = candidates
            .Where(x => x.Type is AgeableDocumentType.Invoice or AgeableDocumentType.PurchaseBill)
            .Select(x => x.Id)
            .ToList();

        if (targetIds.Count == 0)
        {
            return [];
        }

        var fromPayments = await (
                from a in db.PaymentAllocations
                where a.SourceType == DocumentType.Payment
                join p in db.Payments on a.SourceId equals p.Id
                where a.TargetDocumentType == targetType && targetIds.Contains(a.TargetDocumentId)
                      && p.Direction == paymentDirection && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { TargetId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var fromVouchers = await (
                from a in db.PaymentAllocations
                where a.SourceType == DocumentType.JournalVoucher
                join l in db.JournalVoucherLines on a.SourceId equals l.Id
                join v in db.JournalVouchers on l.JournalVoucherId equals v.Id
                where a.TargetDocumentType == targetType && targetIds.Contains(a.TargetDocumentId)
                      && v.OrganizationId == request.OrganizationId && v.Status == JournalVoucherStatus.Approved
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

    private async Task<Dictionary<Guid, decimal>> LoadCreditNoteReductionsAsync(
        DocumentAgeQuery request, List<Candidate> candidates, CancellationToken cancellationToken)
    {
        var invoiceIds = candidates.Where(x => x.Type == AgeableDocumentType.Invoice).Select(x => x.Id).ToList();
        if (invoiceIds.Count == 0)
        {
            return [];
        }

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date <= request.AsOfDate && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId != null && invoiceIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value })
            .ToListAsync(cancellationToken);

        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();
        var lines = await db.CreditNoteLines
            .Where(x => creditNoteIds.Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var note in creditNotes)
        {
            result[note.ReferrerId] = result.GetValueOrDefault(note.ReferrerId) + gross.GetValueOrDefault(note.Id);
        }

        return result;
    }

    private async Task<Dictionary<Guid, decimal>> LoadDebitNoteReductionsAsync(
        DocumentAgeQuery request, List<Candidate> candidates, CancellationToken cancellationToken)
    {
        var billIds = candidates.Where(x => x.Type == AgeableDocumentType.PurchaseBill).Select(x => x.Id).ToList();
        if (billIds.Count == 0)
        {
            return [];
        }

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date <= request.AsOfDate && x.ReferrerType == DocumentType.PurchaseBill
                && x.ReferrerId != null && billIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);

        var debitNoteIds = debitNotes.Select(x => x.Id).ToList();
        var lines = await db.DebitNoteLines
            .Where(x => debitNoteIds.Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var note in debitNotes)
        {
            var net = gross.GetValueOrDefault(note.Id) - note.TdsAmount;
            result[note.ReferrerId] = result.GetValueOrDefault(note.ReferrerId) + net;
        }

        return result;
    }
}
