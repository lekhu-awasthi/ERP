using ErpApp.Domain.Common;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.VatSummaryReport;

public sealed class VatSummaryReportQueryHandler(IAppDbContext db)
    : IRequestHandler<VatSummaryReportQuery, VatSummaryReportDto>
{
    private static readonly VatRate[] AllVatRates = Enum.GetValues<VatRate>();

    public async Task<VatSummaryReportDto> Handle(VatSummaryReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 44 (43 Decision D) -- <b>the VAT return is filed in NPR, so this report reports
        // NPR.</b> Each of the four document types now carries its ExchangeRate alongside its id, and
        // every line is folded to base before it reaches a bucket.
        //
        // Folded per line, before the bucketing. Here that is not a rounding nicety but the report's
        // whole arithmetic: a bucket is `invoice lines at this rate MINUS credit note lines at this
        // rate`, and the totals are sums across buckets, so two documents at different rates must be
        // made commensurable before they are netted against each other -- not after.
        //
        // In memory, after ToListAsync: ExchangeRates.ToBase is a static call, untranslatable on SQL
        // Server and silently evaluated in C# by InMemory, so every handler test would pass while the
        // endpoint 500s (phase-34b).
        var invoiceRates = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ExchangeRate })
            .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken);
        var invoiceIds = invoiceRates.Keys.ToList();
        var invoiceLines = FoldToBase(
            await db.InvoiceLines
                .Where(x => invoiceIds.Contains(x.InvoiceId))
                .Select(x => new RawVatLine(x.InvoiceId, x.VatRate, x.Amount, x.VatAmount))
                .ToListAsync(cancellationToken),
            invoiceRates);

        var creditNoteRates = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ExchangeRate })
            .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken);
        var creditNoteIds = creditNoteRates.Keys.ToList();
        var creditNoteLines = FoldToBase(
            await db.CreditNoteLines
                .Where(x => creditNoteIds.Contains(x.CreditNoteId))
                .Select(x => new RawVatLine(x.CreditNoteId, x.VatRate, x.Amount, x.VatAmount))
                .ToListAsync(cancellationToken),
            creditNoteRates);

        var purchaseBillRates = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ExchangeRate })
            .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken);
        var purchaseBillIds = purchaseBillRates.Keys.ToList();
        var purchaseBillLines = FoldToBase(
            await db.PurchaseBillLines
                .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
                .Select(x => new RawVatLine(x.PurchaseBillId, x.VatRate, x.Amount, x.VatAmount))
                .ToListAsync(cancellationToken),
            purchaseBillRates);

        var debitNoteRates = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ExchangeRate })
            .ToDictionaryAsync(x => x.Id, x => x.ExchangeRate, cancellationToken);
        var debitNoteIds = debitNoteRates.Keys.ToList();
        var debitNoteLines = FoldToBase(
            await db.DebitNoteLines
                .Where(x => debitNoteIds.Contains(x.DebitNoteId))
                .Select(x => new RawVatLine(x.DebitNoteId, x.VatRate, x.Amount, x.VatAmount))
                .ToListAsync(cancellationToken),
            debitNoteRates);

        var salesBuckets = AllVatRates
            .Select(rate => new VatSummarySalesBucketDto(
                rate,
                invoiceLines.Where(l => l.VatRate == rate).Sum(l => l.Amount)
                    - creditNoteLines.Where(l => l.VatRate == rate).Sum(l => l.Amount),
                invoiceLines.Where(l => l.VatRate == rate).Sum(l => l.VatAmount)
                    - creditNoteLines.Where(l => l.VatRate == rate).Sum(l => l.VatAmount)))
            .ToList();

        var purchaseBuckets = AllVatRates
            .Select(rate => new VatSummaryPurchaseBucketDto(
                rate,
                purchaseBillLines.Where(l => l.VatRate == rate).Sum(l => l.Amount)
                    - debitNoteLines.Where(l => l.VatRate == rate).Sum(l => l.Amount),
                purchaseBillLines.Where(l => l.VatRate == rate).Sum(l => l.VatAmount)
                    - debitNoteLines.Where(l => l.VatRate == rate).Sum(l => l.VatAmount)))
            .ToList();

        var totalOutputVat = salesBuckets.Sum(b => b.OutputVatAmount);
        var totalInputVat = purchaseBuckets.Sum(b => b.InputVatAmount);

        return new VatSummaryReportDto(
            request.FromDate, request.ToDate, salesBuckets, purchaseBuckets, totalOutputVat, totalInputVat);
    }

    /// <summary>One line as it comes out of the store, still in its document's own currency and
    /// carrying its parent's id so the document's rate can be found (phase 44).</summary>
    private sealed record RawVatLine(Guid ParentId, VatRate VatRate, decimal Amount, decimal VatAmount);

    /// <summary>The same line in base currency. The parent id is dropped deliberately: past this
    /// point nothing may use it, because nothing past this point is allowed to convert again.</summary>
    private sealed record FoldedVatLine(VatRate VatRate, decimal Amount, decimal VatAmount);

    /// <summary>
    /// Phase 44 (43 Decision D) -- the fold to base currency, in one place all four document types
    /// go through, so no two of them can drift. Each line converts at <b>its own document's</b> rate,
    /// which is why the rate is a per-parent lookup rather than a single argument.
    /// </summary>
    private static List<FoldedVatLine> FoldToBase(
        IReadOnlyList<RawVatLine> lines, IReadOnlyDictionary<Guid, decimal> ratesByParent) =>
    [
        .. lines.Select(x => new FoldedVatLine(
            x.VatRate,
            ExchangeRates.ToBase(x.Amount, ratesByParent[x.ParentId]),
            ExchangeRates.ToBase(x.VatAmount, ratesByParent[x.ParentId]))),
    ];
}
