using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactAgeingSummary;

/// <summary>
/// Outstanding-per-bill netting: NetAmount(bill) minus every Approved Payment allocation targeting
/// it minus every Approved linked reversal (CreditNote for Invoice, DebitNote for PurchaseBill)
/// whose ReferrerId points at it. NetAmount is GrandTotal for an Invoice, GrandTotal-TdsAmount for a
/// PurchaseBill/Expense -- TDS is withheld from what's actually payable to the supplier
/// (PurchaseBillPostingRule's own Credit-AP-net-of-TDS choice, phase-6-status.md), so the payable
/// balance a Supplier Ageing/Statement report should show is the net figure, not the gross bill
/// total. NOTE: GetDefaultPaymentAllocationsQueryHandler (Phase 5/6, Payment's own FIFO-suggestion
/// query) uses PurchaseBill.GrandTotal directly, not net-of-TDS -- a pre-existing latent
/// overstatement in that unrelated handler, not fixed here (out of scope for a pure-read report
/// phase), but worth knowing this report's own "outstanding" figure is the technically-correct one
/// and may diverge from what that suggestion query would offer to allocate. See phase-9-status.md.
///
/// Expense never appears as a Payment-allocation or DebitNote-reversal target anywhere in this
/// codebase (PaymentValidation.EnsureAllocationTargetsExistAsync only recognizes Invoice/PurchaseBill
/// targets, and DebitNote is only ever a PurchaseBill-conversion target) -- so an Expense's payable
/// is always fully outstanding here, a fact about this codebase's existing data model, not a bug
/// introduced by this report.
///
/// A standalone CreditNote/DebitNote (no ReferrerId matching an in-scope bill) reduces the Contact's
/// real balance but isn't bucketed here -- it has no specific bill to attach an age to. It still
/// shows up in ContactStatementQuery's flat ledger, which needs no such attribution. See
/// phase-9-status.md's scope decision for the reasoning (this is the one place Ageing and Statement's
/// totals can legitimately diverge for a Contact with a standalone reversal on file).
/// </summary>
public sealed class ContactAgeingSummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<ContactAgeingSummaryQuery, ContactAgeingSummaryDto>
{
    private sealed record Bill(Guid Id, Guid ContactId, DateOnly Date, decimal NetAmount);

    public async Task<ContactAgeingSummaryDto> Handle(ContactAgeingSummaryQuery request, CancellationToken cancellationToken)
    {
        var bills = request.ContactType == ContactType.Customer
            ? await LoadCustomerBillsAsync(request, cancellationToken)
            : await LoadSupplierBillsAsync(request, cancellationToken);

        var reductionsByBillId = request.ContactType == ContactType.Customer
            ? await LoadCreditNoteReductionsAsync(request, bills, cancellationToken)
            : await LoadDebitNoteReductionsAsync(request, bills, cancellationToken);

        var targetDocumentType = request.ContactType == ContactType.Customer ? DocumentType.Invoice : DocumentType.PurchaseBill;
        var paymentDirection = request.ContactType == ContactType.Customer ? PaymentDirection.Received : PaymentDirection.Paid;
        var billIds = bills.Select(x => x.Id).ToList();
        var allocationsByBillId = await (
                from a in db.PaymentAllocations
                join p in db.Payments on a.PaymentId equals p.Id
                where a.TargetDocumentType == targetDocumentType && billIds.Contains(a.TargetDocumentId)
                      && p.Direction == paymentDirection && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { BillId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.BillId, x => x.Allocated, cancellationToken);

        var contactIds = bills.Select(x => x.ContactId).Distinct().ToList();
        var contactsQuery = db.Contacts.Where(x => x.OrganizationId == request.OrganizationId && x.Type == request.ContactType);
        if (request.ContactGroupId is { } groupId)
        {
            contactsQuery = contactsQuery.Where(x => x.GroupId == groupId);
        }

        var contacts = await contactsQuery
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var buckets = new Dictionary<Guid, decimal[]>();

        foreach (var bill in bills)
        {
            if (!contacts.ContainsKey(bill.ContactId))
            {
                continue; // filtered out by ContactGroupId
            }

            var outstanding = bill.NetAmount
                - allocationsByBillId.GetValueOrDefault(bill.Id)
                - reductionsByBillId.GetValueOrDefault(bill.Id);

            if (outstanding == 0)
            {
                continue;
            }

            var age = request.AsOfDate.DayNumber - bill.Date.DayNumber;
            var bucketIndex = age <= 30 ? 0 : age <= 60 ? 1 : age <= 90 ? 2 : 3;

            if (!buckets.TryGetValue(bill.ContactId, out var contactBuckets))
            {
                contactBuckets = new decimal[4];
                buckets[bill.ContactId] = contactBuckets;
            }

            contactBuckets[bucketIndex] += outstanding;
        }

        var rows = buckets
            .Select(kvp =>
            {
                var contact = contacts[kvp.Key];
                var groupName = contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null;
                return new ContactAgeingSummaryRowDto(
                    contact.Id, contact.Code, contact.Name, groupName,
                    kvp.Value[0], kvp.Value[1], kvp.Value[2], kvp.Value[3]);
            })
            .OrderBy(x => x.ContactCode)
            .ToList();

        return new ContactAgeingSummaryDto(request.AsOfDate, request.ContactType, rows);
    }

    private async Task<List<Bill>> LoadCustomerBillsAsync(ContactAgeingSummaryQuery request, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date })
            .ToListAsync(cancellationToken);

        var lines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var grandTotals = lines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        return invoices
            .Select(x => new Bill(x.Id, x.ContactId, x.Date, grandTotals.GetValueOrDefault(x.Id)))
            .ToList();
    }

    private async Task<List<Bill>> LoadSupplierBillsAsync(ContactAgeingSummaryQuery request, CancellationToken cancellationToken)
    {
        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillGross = purchaseBillLines.GroupBy(x => x.PurchaseBillId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Approved && x.Date <= request.AsOfDate)
            .Select(x => new { x.Id, x.ContactId, x.Date, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var expenseGross = expenseLines.GroupBy(x => x.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var bills = purchaseBills
            .Select(x => new Bill(x.Id, x.ContactId, x.Date, purchaseBillGross.GetValueOrDefault(x.Id) - x.TdsAmount))
            .ToList();
        bills.AddRange(expenses.Select(x => new Bill(x.Id, x.ContactId, x.Date, expenseGross.GetValueOrDefault(x.Id) - x.TdsAmount)));
        return bills;
    }

    private async Task<Dictionary<Guid, decimal>> LoadCreditNoteReductionsAsync(
        ContactAgeingSummaryQuery request, List<Bill> bills, CancellationToken cancellationToken)
    {
        var billIds = bills.Select(x => x.Id).ToList();

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date <= request.AsOfDate && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId != null && billIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value })
            .ToListAsync(cancellationToken);
        var lines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var cn in creditNotes)
        {
            result[cn.ReferrerId] = result.GetValueOrDefault(cn.ReferrerId) + gross.GetValueOrDefault(cn.Id);
        }

        return result;
    }

    private async Task<Dictionary<Guid, decimal>> LoadDebitNoteReductionsAsync(
        ContactAgeingSummaryQuery request, List<Bill> bills, CancellationToken cancellationToken)
    {
        var billIds = bills.Select(x => x.Id).ToList();

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date <= request.AsOfDate && x.ReferrerType == DocumentType.PurchaseBill
                && x.ReferrerId != null && billIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var lines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var dn in debitNotes)
        {
            var net = gross.GetValueOrDefault(dn.Id) - dn.TdsAmount;
            result[dn.ReferrerId] = result.GetValueOrDefault(dn.ReferrerId) + net;
        }

        return result;
    }
}
