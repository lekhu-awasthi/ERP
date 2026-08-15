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
        var invoiceIds = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoiceIds.Contains(x.InvoiceId))
            .Select(x => new { x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNoteIds = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNoteIds.Contains(x.CreditNoteId))
            .Select(x => new { x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var purchaseBillIds = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var debitNoteIds = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNoteIds.Contains(x.DebitNoteId))
            .Select(x => new { x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

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
}
