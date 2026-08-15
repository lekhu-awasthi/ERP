using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

/// <summary>
/// Every Approved document for this Contact contributes its own signed delta directly -- unlike
/// ContactAgeingSummaryQuery, a flat ledger doesn't need to attribute a CreditNote/DebitNote/Payment
/// to a *specific* bill, so a standalone (unlinked) reversal is included here exactly like a linked
/// one. This is the one place Ageing and Statement can legitimately disagree for a Contact carrying a
/// standalone reversal -- see ContactAgeingSummaryQueryHandler's own doc comment.
///
/// Each line set is loaded with its own concrete Where lambda (not a generic helper over
/// IQueryable&lt;TLine&gt;) -- a generic parent-id selector passed as a captured Func can't be
/// translated by EF Core's LINQ provider (it isn't an expression tree the provider can decompose),
/// the same class of gotcha CLAUDE.md's known-gotchas list already documents for a different generic
/// scenario. Repeating five near-identical blocks (matching AnnexThirteenReportQueryHandler's own
/// style) stays simpler and safer than fighting that translation boundary.
/// </summary>
public sealed class ContactStatementQueryHandler(IAppDbContext db) : IRequestHandler<ContactStatementQuery, ContactStatementDto>
{
    private sealed record Event(DateOnly Date, DocumentType DocumentType, string Code, string? Reference, decimal SignedAmount);

    public async Task<ContactStatementDto> Handle(ContactStatementQuery request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == request.ContactId && x.OrganizationId == request.OrganizationId && x.Type == request.ContactType,
                cancellationToken)
            ?? throw new NotFoundException($"{request.ContactType} not found.");

        var events = request.ContactType == ContactType.Customer
            ? await LoadCustomerEventsAsync(request, cancellationToken)
            : await LoadSupplierEventsAsync(request, cancellationToken);

        var openingBalance = contact.OpeningBalance + events
            .Where(x => x.Date < request.FromDate)
            .Sum(x => x.SignedAmount);

        var running = openingBalance;
        var rows = new List<ContactStatementRowDto>();

        foreach (var e in events.Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate)
                     .OrderBy(x => x.Date).ThenBy(x => x.Code))
        {
            running += e.SignedAmount;

            var debit = request.ContactType == ContactType.Customer
                ? Math.Max(e.SignedAmount, 0)
                : Math.Max(-e.SignedAmount, 0);
            var credit = request.ContactType == ContactType.Customer
                ? Math.Max(-e.SignedAmount, 0)
                : Math.Max(e.SignedAmount, 0);

            rows.Add(new ContactStatementRowDto(
                e.Date, e.DocumentType, e.Code, e.Reference, debit, credit, Math.Abs(running), BalanceType(request.ContactType, running)));
        }

        return new ContactStatementDto(
            contact.Id, contact.Code, contact.Name, contact.Type, request.FromDate, request.ToDate,
            Math.Abs(openingBalance), BalanceType(request.ContactType, openingBalance),
            rows, Math.Abs(running), BalanceType(request.ContactType, running));
    }

    private static string BalanceType(ContactType contactType, decimal signedBalance) =>
        contactType == ContactType.Customer
            ? (signedBalance >= 0 ? "DR" : "CR")
            : (signedBalance >= 0 ? "CR" : "DR");

    private async Task<List<Event>> LoadCustomerEventsAsync(ContactStatementQuery request, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Status == InvoiceStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var invoiceTotals = invoiceLines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Status == CreditNoteStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var creditNoteTotals = creditNoteLines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Direction == PaymentDirection.Received && x.Status == PaymentStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Date, x.Code, x.Reference, x.Amount })
            .ToListAsync(cancellationToken);

        var events = new List<Event>();
        events.AddRange(invoices.Select(x => new Event(x.Date, DocumentType.Invoice, x.Code, x.Reference, invoiceTotals.GetValueOrDefault(x.Id))));
        events.AddRange(creditNotes.Select(x => new Event(x.Date, DocumentType.CreditNote, x.Code, x.Reference, -creditNoteTotals.GetValueOrDefault(x.Id))));
        events.AddRange(payments.Select(x => new Event(x.Date, DocumentType.Payment, x.Code, x.Reference, -x.Amount)));
        return events;
    }

    private async Task<List<Event>> LoadSupplierEventsAsync(ContactStatementQuery request, CancellationToken cancellationToken)
    {
        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Status == PurchaseBillStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillTotals = purchaseBillLines.GroupBy(x => x.PurchaseBillId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Status == ExpenseStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.SupplierInvoiceReference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var expenseTotals = expenseLines.GroupBy(x => x.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Status == DebitNoteStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var debitNoteTotals = debitNoteLines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId
                && x.Direction == PaymentDirection.Paid && x.Status == PaymentStatus.Approved && x.Date <= request.ToDate)
            .Select(x => new { x.Date, x.Code, x.Reference, x.Amount })
            .ToListAsync(cancellationToken);

        var events = new List<Event>();
        events.AddRange(purchaseBills.Select(x =>
            new Event(x.Date, DocumentType.PurchaseBill, x.Code, x.Reference, purchaseBillTotals.GetValueOrDefault(x.Id) - x.TdsAmount)));
        events.AddRange(expenses.Select(x =>
            new Event(x.Date, DocumentType.Expense, x.Code, x.SupplierInvoiceReference, expenseTotals.GetValueOrDefault(x.Id) - x.TdsAmount)));
        events.AddRange(debitNotes.Select(x =>
            new Event(x.Date, DocumentType.DebitNote, x.Code, x.Reference, -(debitNoteTotals.GetValueOrDefault(x.Id) - x.TdsAmount))));
        events.AddRange(payments.Select(x => new Event(x.Date, DocumentType.Payment, x.Code, x.Reference, -x.Amount)));
        return events;
    }
}
