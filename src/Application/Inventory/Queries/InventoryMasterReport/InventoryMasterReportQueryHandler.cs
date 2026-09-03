using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Reports;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.InventoryMasterReport;

public sealed class InventoryMasterReportQueryHandler(IAppDbContext db)
    : IRequestHandler<InventoryMasterReportQuery, InventoryMasterReportDto>
{
    /// <summary>A line before its document-level and product-level facts are attached.</summary>
    private sealed record Line(
        DocumentType DocumentType,
        Guid DocumentId,
        DateOnly Date,
        Guid ProductId,
        decimal Quantity,
        decimal Rate,
        decimal ItemDiscount,
        decimal TransactionDiscount,
        decimal NetAmount,
        decimal VatAmount)
    {
        /// <summary>Gross line value, so that Amount - (ItemDiscount + TransactionDiscount) ==
        /// NetAmount exactly -- the identity <c>TradeLineReader</c> established against live rows.</summary>
        public decimal Amount => NetAmount + ItemDiscount + TransactionDiscount;

        public decimal TotalAmount => NetAmount + VatAmount;
    }

    public async Task<InventoryMasterReportDto> Handle(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var wanted = request.DocumentType;
        bool Include(DocumentType type) => wanted is null || wanted == type;

        var lines = new List<Line>();

        if (Include(DocumentType.Invoice))
        {
            lines.AddRange(await LoadSalesLinesAsync(request, sign: -1, cancellationToken));
        }

        if (Include(DocumentType.CreditNote))
        {
            lines.AddRange(await LoadCreditNoteLinesAsync(request, cancellationToken));
        }

        if (Include(DocumentType.PurchaseBill))
        {
            lines.AddRange(await LoadPurchaseBillLinesAsync(request, cancellationToken));
        }

        if (Include(DocumentType.DebitNote))
        {
            lines.AddRange(await LoadDebitNoteLinesAsync(request, cancellationToken));
        }

        // The two non-trading types carry no counterparty and no money beyond a cost, so a contact
        // filter necessarily excludes them -- asking "what did this supplier do" cannot be answered
        // by an adjustment nobody made with anybody.
        if (request.ContactId is null && Include(DocumentType.InventoryAdjustment))
        {
            lines.AddRange(await LoadAdjustmentLinesAsync(request, cancellationToken));
        }

        if (request.ContactId is null && Include(DocumentType.ProductionJournal))
        {
            lines.AddRange(await LoadProductionJournalLinesAsync(request, cancellationToken));
        }

        if (request.ProductId is { } productFilter)
        {
            lines = lines.Where(l => l.ProductId == productFilter).ToList();
        }

        var products = await InventoryReportProducts.LoadAsync(
            db, request.OrganizationId, categoryId: null, productId: null, cancellationToken);

        var resolver = await StockSourceDocumentResolver.LoadAsync(
            db, request.OrganizationId, [.. lines.Select(l => (l.DocumentType, l.DocumentId))], cancellationToken);

        var warehouses = await WarehouseNamesByLineAsync(request.OrganizationId, lines, cancellationToken);
        var accounts = await AccountNamesByProductAsync(request.OrganizationId, cancellationToken);

        var rows = lines
            .OrderByDescending(l => l.Date)
            .ThenBy(l => l.DocumentType)
            .ThenBy(l => l.DocumentId)
            .Select(line =>
            {
                var product = products.For(line.ProductId);
                var document = resolver.For(line.DocumentType, line.DocumentId);
                return new InventoryMasterRowDto(
                    line.Date,
                    document?.ContactName,
                    line.DocumentType,
                    line.DocumentId,
                    warehouses.GetValueOrDefault((line.DocumentId, line.ProductId)),
                    AccountFor(line.DocumentType, line.ProductId, accounts),
                    document?.Code ?? string.Empty,
                    document?.Reference,
                    line.ProductId,
                    product?.Display ?? string.Empty,
                    product?.CategoryName ?? string.Empty,
                    line.Quantity,
                    product?.Unit ?? string.Empty,
                    line.Rate,
                    line.Amount,
                    line.ItemDiscount,
                    line.TransactionDiscount,
                    line.NetAmount,
                    line.VatAmount,
                    line.TotalAmount,
                    // See the query's remarks: additional cost is not modelled by this codebase.
                    AdditionalCost: 0);
            })
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new InventoryMasterReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(r => r.NetAmount), rows.Sum(r => r.VatAmount), rows.Sum(r => r.TotalAmount));
    }

    private async Task<List<Line>> LoadSalesLinesAsync(
        InventoryMasterReportQuery request, int sign, CancellationToken cancellationToken)
    {
        var query = db.Invoices.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        var documents = await query.Select(x => new { x.Id, x.Date }).ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = await db.InvoiceLines
            .Where(l => ids.Contains(l.InvoiceId))
            .Select(l => new { l.InvoiceId, l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount })
            .ToListAsync(cancellationToken);

        return [.. lines.Select(l => Build(
            DocumentType.Invoice, l.InvoiceId, dates[l.InvoiceId], l.ProductId,
            sign * l.Quantity, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))];
    }

    private async Task<List<Line>> LoadCreditNoteLinesAsync(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.CreditNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        var documents = await query.Select(x => new { x.Id, x.Date }).ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = await db.CreditNoteLines
            .Where(l => ids.Contains(l.CreditNoteId))
            .Select(l => new { l.CreditNoteId, l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount })
            .ToListAsync(cancellationToken);

        // Positive: a credit note puts stock back.
        return [.. lines.Select(l => Build(
            DocumentType.CreditNote, l.CreditNoteId, dates[l.CreditNoteId], l.ProductId,
            l.Quantity, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))];
    }

    private async Task<List<Line>> LoadPurchaseBillLinesAsync(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseBills.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        var documents = await query.Select(x => new { x.Id, x.Date }).ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = await db.PurchaseBillLines
            .Where(l => ids.Contains(l.PurchaseBillId))
            .Select(l => new { l.PurchaseBillId, l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount })
            .ToListAsync(cancellationToken);

        return [.. lines.Select(l => Build(
            DocumentType.PurchaseBill, l.PurchaseBillId, dates[l.PurchaseBillId], l.ProductId,
            l.Quantity, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))];
    }

    private async Task<List<Line>> LoadDebitNoteLinesAsync(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.DebitNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        var documents = await query.Select(x => new { x.Id, x.Date }).ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = await db.DebitNoteLines
            .Where(l => ids.Contains(l.DebitNoteId))
            .Select(l => new { l.DebitNoteId, l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount })
            .ToListAsync(cancellationToken);

        // Negative: a debit note returns goods to the supplier.
        return [.. lines.Select(l => Build(
            DocumentType.DebitNote, l.DebitNoteId, dates[l.DebitNoteId], l.ProductId,
            -l.Quantity, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))];
    }

    private async Task<List<Line>> LoadAdjustmentLinesAsync(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var documents = await db.InventoryAdjustments
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == InventoryAdjustmentStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date })
            .ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = await db.InventoryAdjustmentLines
            .Where(l => ids.Contains(l.InventoryAdjustmentId))
            .Select(l => new { l.InventoryAdjustmentId, l.ProductId, l.Direction, l.Quantity, l.UnitCost, l.ConsumedUnitCost })
            .ToListAsync(cancellationToken);

        return [.. lines.Select(l =>
        {
            var increase = l.Direction == InventoryAdjustmentDirection.Increase;
            // A decrease is valued at what the FIFO walk actually consumed, never at the entered
            // rate -- the same figure the GL posting used (phase 7).
            var unitCost = increase ? l.UnitCost : l.ConsumedUnitCost ?? l.UnitCost;
            return new Line(
                DocumentType.InventoryAdjustment, l.InventoryAdjustmentId, dates[l.InventoryAdjustmentId],
                l.ProductId, increase ? l.Quantity : -l.Quantity, unitCost,
                ItemDiscount: 0, TransactionDiscount: 0, NetAmount: l.Quantity * unitCost, VatAmount: 0);
        })];
    }

    private async Task<List<Line>> LoadProductionJournalLinesAsync(
        InventoryMasterReportQuery request, CancellationToken cancellationToken)
    {
        var documents = await db.ProductionJournals
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == Domain.Manufacturing.ProductionJournalStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.Date, x.ProductId, x.OutputQuantity, x.FinishedGoodsUnitCost })
            .ToListAsync(cancellationToken);
        var ids = documents.Select(x => x.Id).ToList();
        var dates = documents.ToDictionary(x => x.Id, x => x.Date);

        var lines = new List<Line>();

        // The finished good: one row per journal, from the header rather than a line table.
        lines.AddRange(documents.Select(d => new Line(
            DocumentType.ProductionJournal, d.Id, d.Date, d.ProductId,
            d.OutputQuantity, d.FinishedGoodsUnitCost ?? 0,
            0, 0, d.OutputQuantity * (d.FinishedGoodsUnitCost ?? 0), 0)));

        var rawMaterials = await db.ProductionJournalRawMaterialLines
            .Where(l => ids.Contains(l.ProductionJournalId))
            .Select(l => new { l.ProductionJournalId, l.ProductId, l.Quantity, l.ConsumedUnitCost, l.Amount })
            .ToListAsync(cancellationToken);

        lines.AddRange(rawMaterials.Select(l => new Line(
            DocumentType.ProductionJournal, l.ProductionJournalId, dates[l.ProductionJournalId], l.ProductId,
            -l.Quantity, l.ConsumedUnitCost ?? 0, 0, 0, l.Amount ?? 0, 0)));

        var byProducts = await db.ProductionJournalByProductLines
            .Where(l => ids.Contains(l.ProductionJournalId))
            .Select(l => new { l.ProductionJournalId, l.ProductId, l.Quantity, l.AllocatedUnitCost, l.AllocatedAmount })
            .ToListAsync(cancellationToken);

        lines.AddRange(byProducts.Select(l => new Line(
            DocumentType.ProductionJournal, l.ProductionJournalId, dates[l.ProductionJournalId], l.ProductId,
            l.Quantity, l.AllocatedUnitCost ?? 0, 0, 0, l.AllocatedAmount ?? 0, 0)));

        return lines;
    }

    /// <summary>
    /// The warehouse each (document, product) pair actually moved stock in, read from
    /// <c>StockMovement</c>. See the query's remarks for why this is not the document header: two
    /// of the six types have no warehouse of their own, and a service line has no warehouse at all.
    /// </summary>
    private async Task<Dictionary<(Guid DocumentId, Guid ProductId), string>> WarehouseNamesByLineAsync(
        Guid organizationId, IReadOnlyCollection<Line> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var documentIds = lines.Select(l => l.DocumentId).Distinct().ToList();

        var movements = await db.StockMovements
            .Where(m => m.OrganizationId == organizationId && documentIds.Contains(m.SourceDocumentId))
            .Select(m => new { m.SourceDocumentId, m.ProductId, m.WarehouseId })
            .ToListAsync(cancellationToken);

        var warehouses = await db.Warehouses
            .Where(w => w.OrganizationId == organizationId)
            .Select(w => new { w.Id, w.Name })
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        return movements
            .GroupBy(m => (m.SourceDocumentId, m.ProductId))
            .ToDictionary(
                g => g.Key,
                g => warehouses.GetValueOrDefault(g.First().WarehouseId, string.Empty));
    }

    private async Task<Dictionary<Guid, (string? Sales, string? Purchase)>> AccountNamesByProductAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var products = await db.Products
            .Where(p => p.OrganizationId == organizationId)
            .Select(p => new { p.Id, p.SalesAccountId, p.PurchaseAccountId })
            .ToListAsync(cancellationToken);

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == organizationId)
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        string? Name(Guid? id) => id is { } value ? accounts.GetValueOrDefault(value) : null;

        return products.ToDictionary(p => p.Id, p => (Name(p.SalesAccountId), Name(p.PurchaseAccountId)));
    }

    /// <summary>
    /// Which side's account a row shows. A Credit Note reports the <i>sales</i> account and a Debit
    /// Note the <i>purchase</i> one -- the side the trade happened on, not a return account. That is
    /// what the live report prints ("Sales Goods" on a Credit Note row), and the Sales Return /
    /// Purchase Return account mappings on <c>Product</c> stay unread here.
    /// </summary>
    private static string? AccountFor(
        DocumentType type, Guid productId, Dictionary<Guid, (string? Sales, string? Purchase)> accounts)
    {
        if (!accounts.TryGetValue(productId, out var mapped))
        {
            return null;
        }

        return type switch
        {
            DocumentType.Invoice or DocumentType.CreditNote => mapped.Sales,
            DocumentType.PurchaseBill or DocumentType.DebitNote => mapped.Purchase,
            // Adjustments and production journals show a blank Account cell on the live report.
            _ => null,
        };
    }

    /// <summary>
    /// Rebuilds phase-16b's discount split the way <c>TradeLineReader</c> does: the stored
    /// <c>Line.Amount</c> is already netted of both discounts, so the header discount's share of
    /// this line is recovered by difference.
    /// </summary>
    private static Line Build(
        DocumentType type, Guid documentId, DateOnly date, Guid productId,
        decimal signedQuantity, decimal quantity, decimal rate, decimal discountPct,
        decimal netAmount, decimal vatAmount)
    {
        var gross = quantity * rate;
        var itemDiscount = gross * discountPct / 100m;
        var transactionDiscount = gross - itemDiscount - netAmount;

        return new Line(
            type, documentId, date, productId, signedQuantity, rate,
            itemDiscount, transactionDiscount, netAmount, vatAmount);
    }
}
