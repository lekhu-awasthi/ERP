using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Phase 61 -- what approving an invoice <i>does</i>, once the caller has decided it may: draw the
/// number, flip the status, relieve the stock and post the sale entry. Extracted from
/// <c>ApproveInvoiceCommandHandler</c> so the till's create-approved sale and the ERP's Approve take
/// <b>one</b> path to the ledger rather than two that agree today.
///
/// <para><b>What stays with each caller</b> is the decision, because the two decide differently:
/// the stock gate (same policy, but the till confirms its warning in one request), and the credit
/// check, which an ERP approve runs on the whole total and a till sale only on the part left on
/// credit. Serial numbers are handed in for the same reason: the ERP reads the rows its draft saved,
/// while a till sale writes its serial rows in the same save and so cannot read them back yet.</para>
///
/// <para>Nothing is saved here. The caller's single <c>SaveChangesAsync</c> commits the approval,
/// the stock movements and the entry together, as phase 7 required of the approve.</para>
/// </summary>
internal static class InvoiceApprovalPosting
{
    public static async Task<GlJournalEntry> ApproveAndPostAsync(
        IAppDbContext db,
        IDocumentNumberGenerator numberGenerator,
        IGlPostingRule<InvoicePostingInput> postingRule,
        IStockLedgerService stockLedgerService,
        Guid approvedByUserId,
        Invoice invoice,
        IReadOnlyDictionary<Guid, List<string>> serialsByLine,
        CancellationToken cancellationToken)
    {
        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == invoice.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var goodsLines = invoice.Lines
            .Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods)
            .ToList();

        // Phase 28 (FR-2.5): the fold. The document stores its amounts in its own currency; the
        // general ledger is denominated in the base currency, so every line amount is converted
        // here, before the posting rule runs. Doing it here rather than on the finished GlLineInput
        // list is what keeps the entry balanced by construction -- the rule derives its balancing
        // leg as a sum of these very numbers. See ExchangeRates' doc comment. (A till sale is always
        // the base currency at rate 1 -- Decision E -- so for it the fold is the identity.)
        var postingInput = await InvoiceAccountResolver.ResolveAsync(
            db, invoice.OrganizationId,
            invoice.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, invoice.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, invoice.ExchangeRate),
                ExchangeRates.ToBase(x.ServiceChargeAmount, invoice.ExchangeRate))),
            resolveInventoryAccounts: goodsLines.Count > 0, cancellationToken);

        // Phase 61 -- the till's two legs, resolved only when present and before the number is drawn,
        // so a missing account is a 409 that has consumed nothing.
        if (invoice.ServiceChargeTotal > 0)
        {
            postingInput = postingInput with
            {
                ServiceChargeAccountId = await PosAccountResolver.ServiceChargeAccountAsync(
                    db, invoice.OrganizationId, invoice.LocationId, cancellationToken),
            };
        }

        if (invoice.RoundOff != 0)
        {
            postingInput = postingInput with
            {
                RoundOff = ExchangeRates.ToBase(invoice.RoundOff, invoice.ExchangeRate),
                RoundingAccountId = await PosAccountResolver.RoundingAccountAsync(
                    db, invoice.OrganizationId, invoice.LocationId, cancellationToken),
            };
        }

        var code = await numberGenerator.GetNextNumberAsync(
            invoice.OrganizationId, DocumentType.Invoice, cancellationToken, invoice.LocationId);

        invoice.Approve(approvedByUserId, code);

        var totalCogs = 0m;
        foreach (var line in goodsLines)
        {
            // Phase 51 -- line.BatchId narrows the FIFO walk to one batch when the line named one,
            // and the serials (if any) turn this into one call per physical unit. A line that names
            // neither takes exactly the path it took before that phase.
            var consumption = await LineStockAllocator.ConsumeLineAsync(
                stockLedgerService, invoice.OrganizationId, line.ProductId, invoice.WarehouseId,
                line.PrimaryQuantity, line.BatchId,
                serialsByLine.TryGetValue(line.Id, out var serials) ? serials : [],
                DocumentType.Invoice, invoice.Id, invoice.Date, cancellationToken, invoice.LocationId,
                // Phase 37 -- the Negative Item Balance setting made real. The caller's gate has
                // already turned it into a verdict (and, on Warn, has already been confirmed), so a
                // shortfall reaching here is one the tenant has asked for: it becomes a shortfall
                // layer at the product's last known cost rather than a 409 from the engine that
                // would have made Warn and Do Nothing indistinguishable from Reject.
                allowNegative: true);

            line.RecordCogsUnitCost(consumption.AverageUnitCost);
            // Phase 52 -- COGS is priced per PRIMARY unit: AverageUnitCost came off FIFO layers,
            // which are always denominated in primary units. A line entered in cartons would
            // otherwise be costed as though it had shipped cartons' worth of pieces.
            totalCogs += line.PrimaryQuantity.Value * consumption.AverageUnitCost;
        }

        if (totalCogs > 0)
        {
            postingInput = postingInput with { CogsAmount = totalCogs };
        }

        var glEntry = GlJournalEntry.Post(
            invoice.OrganizationId, DocumentType.Invoice, invoice.Id, postingRule.BuildLines(postingInput),
            invoice.LocationId);
        db.GlJournalEntries.Add(glEntry);

        return glEntry;
    }
}
