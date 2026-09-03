using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Trade;

/// <summary>
/// The line-level fact set every phase-26b trade-analytics report groups: approved Invoice and
/// CreditNote lines on the Sales side, approved PurchaseBill and DebitNote lines on the Purchase
/// side, over one date range. Four report pairs -- By Customer, By Item, and their two Monthly
/// crosstabs -- differ only in how they group these facts, so the loading happens once here.
///
/// <para><b>Returns are negative facts, not separate rows.</b> A CreditNote/DebitNote line
/// contributes its values negated, so every figure these reports show is net of returns. That is
/// what the live reports do: the Sales Summary Report prints negative rows on days whose returns
/// exceeded their sales, which is only possible if returns are folded into the same measure rather
/// than listed alongside it. It is also the one place these reports diverge from the Sales/Purchase
/// Master Reports, which keep returns positive and put a Type column beside them because they are a
/// register rather than an analysis.</para>
///
/// <para><b>The discount columns are reconstructed exactly as phase-16b defined them</b>, and the
/// arithmetic is copied from <c>SalesMasterReportQueryHandler</c> rather than re-derived: Amount is
/// Quantity x Rate net of the line's own DiscountPct, ItemDiscount is the line discount,
/// TransactionDiscount is this line's share of the document-header discount (obtained by
/// difference, since the stored <c>Line.Amount</c> is already fully netted), and NetAmount is that
/// stored value. The live reports show a single <b>Discount</b> column, which is the two added
/// together.</para>
///
/// <para>Each document type is loaded with its own concrete <c>Where</c>, never a generic helper
/// parameterised by a captured <c>Func</c> -- phase-9 bug #1.</para>
/// </summary>
internal static class TradeLineReader
{
    /// <summary>
    /// One document line, signed. <paramref name="Quantity"/> is negative for a return, so a
    /// product's net quantity for the period is a plain sum.
    /// </summary>
    internal sealed record Fact(
        Guid ContactId,
        Guid ProductId,
        DateOnly Date,
        VatRate VatRate,
        decimal Quantity,
        decimal Amount,
        decimal Discount,
        decimal NetAmount,
        decimal VatAmount)
    {
        public decimal TotalAmount => NetAmount + VatAmount;
    }

    internal static Task<List<Fact>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        TradeSide side,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) =>
        side == TradeSide.Sales
            ? LoadSalesAsync(db, organizationId, fromDate, toDate, cancellationToken)
            : LoadPurchaseAsync(db, organizationId, fromDate, toDate, cancellationToken);

    private static async Task<List<Fact>> LoadSalesAsync(
        IAppDbContext db, Guid organizationId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == organizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= fromDate && x.Date <= toDate)
            .Select(x => new { x.Id, x.ContactId, x.Date })
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoiceIds.Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.ProductId, x.Quantity, x.Rate, x.DiscountPct, x.Amount, x.VatAmount, x.VatRate })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= fromDate && x.Date <= toDate)
            .Select(x => new { x.Id, x.ContactId, x.Date })
            .ToListAsync(cancellationToken);
        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNoteIds.Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.ProductId, x.Quantity, x.Rate, x.DiscountPct, x.Amount, x.VatAmount, x.VatRate })
            .ToListAsync(cancellationToken);

        var invoicesById = invoices.ToDictionary(x => x.Id);
        var creditNotesById = creditNotes.ToDictionary(x => x.Id);

        var facts = new List<Fact>(invoiceLines.Count + creditNoteLines.Count);

        foreach (var line in invoiceLines)
        {
            var document = invoicesById[line.InvoiceId];
            facts.Add(BuildFact(
                document.ContactId, line.ProductId, document.Date,
                line.VatRate, line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount, sign: 1));
        }

        foreach (var line in creditNoteLines)
        {
            var document = creditNotesById[line.CreditNoteId];
            facts.Add(BuildFact(
                document.ContactId, line.ProductId, document.Date,
                line.VatRate, line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount, sign: -1));
        }

        return facts;
    }

    private static async Task<List<Fact>> LoadPurchaseAsync(
        IAppDbContext db, Guid organizationId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var bills = await db.PurchaseBills
            .Where(x => x.OrganizationId == organizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= fromDate && x.Date <= toDate)
            .Select(x => new { x.Id, x.ContactId, x.Date })
            .ToListAsync(cancellationToken);
        var billIds = bills.Select(x => x.Id).ToList();
        var billLines = await db.PurchaseBillLines
            .Where(x => billIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Quantity, x.Rate, x.DiscountPct, x.Amount, x.VatAmount, x.VatRate })
            .ToListAsync(cancellationToken);

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == DebitNoteStatus.Approved
                && x.Date >= fromDate && x.Date <= toDate)
            .Select(x => new { x.Id, x.ContactId, x.Date })
            .ToListAsync(cancellationToken);
        var debitNoteIds = debitNotes.Select(x => x.Id).ToList();
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNoteIds.Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Quantity, x.Rate, x.DiscountPct, x.Amount, x.VatAmount, x.VatRate })
            .ToListAsync(cancellationToken);

        var billsById = bills.ToDictionary(x => x.Id);
        var debitNotesById = debitNotes.ToDictionary(x => x.Id);

        var facts = new List<Fact>(billLines.Count + debitNoteLines.Count);

        foreach (var line in billLines)
        {
            var document = billsById[line.PurchaseBillId];
            facts.Add(BuildFact(
                document.ContactId, line.ProductId, document.Date,
                line.VatRate, line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount, sign: 1));
        }

        foreach (var line in debitNoteLines)
        {
            var document = debitNotesById[line.DebitNoteId];
            facts.Add(BuildFact(
                document.ContactId, line.ProductId, document.Date,
                line.VatRate, line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount, sign: -1));
        }

        return facts;
    }

    private static Fact BuildFact(
        Guid contactId, Guid productId, DateOnly date, VatRate vatRate,
        decimal quantity, decimal rate, decimal discountPct, decimal netAmount, decimal vatAmount, int sign)
    {
        var gross = quantity * rate;
        var itemDiscount = gross * discountPct / 100m;
        var afterLineDiscount = gross - itemDiscount;
        var transactionDiscount = afterLineDiscount - netAmount;

        // Amount is the *gross* line value, so that Amount - Discount == NetAmount exactly. That
        // identity is not an assumption: it is arithmetic the live reports satisfy on every row
        // read on 2026-09-03 (a customer at Amount 50,000, Discount 5,000, Net Sales 45,000; the
        // Sales Summary's Bhadra 2083 row at Sub Total 41,987.95 less Discount 4,950 equalling its
        // two sales buckets' 37,037.95). Discount carries the line and header discounts together,
        // which is the single Discount column those reports show.
        return new Fact(
            contactId,
            productId,
            date,
            vatRate,
            sign * quantity,
            sign * gross,
            sign * (itemDiscount + transactionDiscount),
            sign * netAmount,
            sign * vatAmount);
    }
}
