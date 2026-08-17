using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

/// <summary>
/// The event-loading + signed-delta computation both ContactStatementQueryHandler and
/// ContactOverviewQueryHandler need -- extracted here rather than duplicated once a second caller
/// appeared. Kept as static functions over an explicit (db, organizationId, contactType, contactId,
/// toDate) parameter list, not an instance/DI service -- there's no state to hold, and a static
/// function is the smallest thing that satisfies two callers without inventing a new abstraction
/// layer for what's still just a handful of lines (see phase-10-status.md's reuse-vs-duplicate call).
///
/// Each line set is loaded with its own concrete Where lambda, not a generic helper over
/// IQueryable&lt;TLine&gt; -- a generic parent-id selector passed as a captured Func can't be
/// translated by EF Core's LINQ provider, the same gotcha phase-9-status.md already hit once here.
/// </summary>
internal static class ContactLedgerReader
{
    internal sealed record Event(DateOnly Date, DocumentType DocumentType, string Code, string? Reference, decimal SignedAmount);

    internal static Task<List<Event>> LoadEventsAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, Guid contactId, DateOnly toDate, CancellationToken cancellationToken) =>
        contactType == ContactType.Customer
            ? LoadCustomerEventsAsync(db, organizationId, contactId, toDate, cancellationToken)
            : LoadSupplierEventsAsync(db, organizationId, contactId, toDate, cancellationToken);

    internal static string BalanceType(ContactType contactType, decimal signedBalance) =>
        contactType == ContactType.Customer
            ? (signedBalance >= 0 ? "DR" : "CR")
            : (signedBalance >= 0 ? "CR" : "DR");

    private static async Task<List<Event>> LoadCustomerEventsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, DateOnly toDate, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Status == InvoiceStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var invoiceTotals = invoiceLines.GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Status == CreditNoteStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var creditNoteTotals = creditNoteLines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Direction == PaymentDirection.Received && x.Status == PaymentStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Date, x.Code, x.Reference, x.Amount })
            .ToListAsync(cancellationToken);

        var events = new List<Event>();
        events.AddRange(invoices.Select(x => new Event(x.Date, DocumentType.Invoice, x.Code, x.Reference, invoiceTotals.GetValueOrDefault(x.Id))));
        events.AddRange(creditNotes.Select(x => new Event(x.Date, DocumentType.CreditNote, x.Code, x.Reference, -creditNoteTotals.GetValueOrDefault(x.Id))));
        events.AddRange(payments.Select(x => new Event(x.Date, DocumentType.Payment, x.Code, x.Reference, -x.Amount)));
        return events;
    }

    private static async Task<List<Event>> LoadSupplierEventsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, DateOnly toDate, CancellationToken cancellationToken)
    {
        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Status == PurchaseBillStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillTotals = purchaseBillLines.GroupBy(x => x.PurchaseBillId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Status == ExpenseStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.SupplierInvoiceReference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var expenseTotals = expenseLines.GroupBy(x => x.ExpenseId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Status == DebitNoteStatus.Approved && x.Date <= toDate)
            .Select(x => new { x.Id, x.Date, x.Code, x.Reference, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var debitNoteTotals = debitNoteLines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var payments = await db.Payments
            .Where(x => x.OrganizationId == organizationId && x.ContactId == contactId
                && x.Direction == PaymentDirection.Paid && x.Status == PaymentStatus.Approved && x.Date <= toDate)
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
