using System.Globalization;
using System.Text;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Alerts;

/// <summary>
/// "Daily Transaction Summary" (erp-module-scan.md Configurations §15). A count-and-total rollup of
/// the tenant's approved trading activity for one business day.
///
/// <para><b>The content is deliberately bounded to aggregates.</b> Nothing per-transaction, no
/// contact names, no PAN, no document codes -- because these figures leave the tenant to whatever
/// addresses an admin typed into the Recipients box, and those addresses were never permission-
/// checked against anything. Keeping the payload at the level of "12 invoices, NPR 340,000" means
/// the worst case of a mis-typed recipient is a leaked daily turnover figure, not a customer list.
/// If a future alert type needs per-row detail, that is a scope decision to take deliberately, not
/// something to widen here by accident. See docs/phase-20e-status.md, Decision B.</para>
///
/// <para>Only <c>Approved</c> documents count -- a Draft has no document number and no GL effect
/// (CLAUDE.md's Draft-then-Approve lifecycle), and a Void one has been reversed, so counting either
/// would make the summary disagree with every report in the product. Documents are matched on their
/// own business <c>Date</c>, not on CreatedAt/ApprovedAt: a bill entered today for yesterday's date
/// belongs to yesterday's business day, which is the same rule the registers use.</para>
/// </summary>
public sealed class DailyTransactionSummaryContentBuilder(IAppDbContext db) : IAlertContentBuilder
{
    public AlertType AlertType => AlertType.DailyTransactionSummary;

    public async Task<AlertContent> BuildAsync(
        Guid organizationId, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        var organizationName = await db.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Your organization";

        var invoiceIds = await db.Invoices
            .Where(x => x.OrganizationId == organizationId && x.Date == occurrenceDate && x.Status == InvoiceStatus.Approved)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var invoiceTotal = await db.InvoiceLines
            .Where(l => invoiceIds.Contains(l.InvoiceId))
            .SumAsync(l => (decimal?)(l.Amount + l.VatAmount), cancellationToken) ?? 0m;

        var creditNoteIds = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.Date == occurrenceDate && x.Status == CreditNoteStatus.Approved)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var creditNoteTotal = await db.CreditNoteLines
            .Where(l => creditNoteIds.Contains(l.CreditNoteId))
            .SumAsync(l => (decimal?)(l.Amount + l.VatAmount), cancellationToken) ?? 0m;

        var purchaseBillIds = await db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId && x.Date == occurrenceDate
                        && x.Status == Domain.Purchasing.PurchaseBillStatus.Approved)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var purchaseBillTotal = await db.PurchaseBillLines
            .Where(l => purchaseBillIds.Contains(l.PurchaseBillId))
            .SumAsync(l => (decimal?)(l.Amount + l.VatAmount), cancellationToken) ?? 0m;

        var debitNoteIds = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && x.Date == occurrenceDate
                        && x.Status == Domain.Purchasing.DebitNoteStatus.Approved)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var debitNoteTotal = await db.DebitNoteLines
            .Where(l => debitNoteIds.Contains(l.DebitNoteId))
            .SumAsync(l => (decimal?)(l.Amount + l.VatAmount), cancellationToken) ?? 0m;

        var payments = await db.Payments
            .Where(x => x.OrganizationId == organizationId && x.Date == occurrenceDate && x.Status == PaymentStatus.Approved)
            .Select(x => new { x.Direction, x.Amount })
            .ToListAsync(cancellationToken);

        var received = payments.Where(p => p.Direction == PaymentDirection.Received).ToList();
        var paid = payments.Where(p => p.Direction == PaymentDirection.Paid).ToList();

        var rows = new (string Label, int Count, decimal Total)[]
        {
            ("Sales Invoices", invoiceIds.Count, invoiceTotal),
            ("Credit Notes", creditNoteIds.Count, creditNoteTotal),
            ("Purchase Bills", purchaseBillIds.Count, purchaseBillTotal),
            ("Debit Notes", debitNoteIds.Count, debitNoteTotal),
            ("Receipts (money in)", received.Count, received.Sum(p => p.Amount)),
            ("Payments (money out)", paid.Count, paid.Sum(p => p.Amount)),
        };

        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"Daily Transaction Summary for {organizationName}");
        body.AppendLine(CultureInfo.InvariantCulture, $"Business day: {occurrenceDate:yyyy-MM-dd} (Nepal time)");
        body.AppendLine();

        foreach (var (label, count, total) in rows)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{label,-22} {count,5}   NPR {total,16:N2}");
        }

        body.AppendLine();
        body.AppendLine("Approved documents only. Drafts and voided documents are excluded.");
        body.AppendLine("You are receiving this because an administrator scheduled it in Configurations > Alert Scheduler.");

        return new AlertContent(
            $"Daily Transaction Summary - {organizationName} - {occurrenceDate:yyyy-MM-dd}",
            body.ToString());
    }
}
