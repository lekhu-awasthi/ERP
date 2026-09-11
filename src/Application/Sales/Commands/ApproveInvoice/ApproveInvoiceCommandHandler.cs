using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveInvoice;

/// <summary>
/// Phase 7: the stock decrement is real now. For every Goods line (a Service line never touches
/// stock -- Product.Type gate, same as every other Goods-only behavior in this codebase),
/// IStockLedgerService.ConsumeAsync walks FIFO layers scoped to the invoice's own WarehouseId and
/// returns the weighted-average cost consumed; summed across lines that's the invoice's COGS,
/// posted as a second Debit COGS/Credit Inventory pair alongside the existing Sales/AR/VAT lines
/// (see InvoicePostingRule). Both the stock mutation and the GL posting happen before the single
/// SaveChangesAsync at the end -- one transaction, per roadmap Phase 7 task 4's explicit
/// requirement.
/// </summary>
public sealed class ApproveInvoiceCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<InvoicePostingInput> postingRule,
    IStockAvailabilityPolicy stockAvailabilityPolicy,
    IStockLedgerService stockLedgerService,
    ICreditLimitPolicy creditLimitPolicy)
    : IRequestHandler<ApproveInvoiceCommand, ApproveInvoiceResult>
{
    public async Task<ApproveInvoiceResult> Handle(ApproveInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new ConflictException("Only a Draft invoice can be approved.");
        }

        if (invoice.Lines.Count == 0)
        {
            throw new ConflictException("An invoice needs at least one line to be approved.");
        }

        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var goodsLines = invoice.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

        var stockStatus = await stockAvailabilityPolicy.CheckAsync(invoice, cancellationToken);
        if (stockStatus == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException(
                "Insufficient stock to approve this invoice. Adjust the quantities or add stock before approving.");
        }

        if (stockStatus == StockAvailabilityStatus.Warn && !request.OverrideWarning)
        {
            throw new StockAvailabilityWarningException(
                "One or more lines on this invoice exceed the available stock in the selected warehouse. " +
                "Approve again to continue anyway.");
        }

        // Phase 31 (credit control). Checked here -- after the stock gate, before the document
        // number is drawn and before anything is posted -- because that is where the live product
        // checks it: saving a breaching draft raises nothing at all, and the "Crossed Credit Limit"
        // dialog appears on Approve (confirmed live 2026-09-06). Stock first, so an invoice tripping
        // both surfaces them in the same order the reference product does.
        var creditStatus = await creditLimitPolicy.CheckAsync(
            request.OrganizationId, invoice.ContactId, invoice.GrandTotal, cancellationToken);

        if (creditStatus.Status == CreditLimitStatus.Reject)
        {
            throw new ConflictException(
                $"Approving this invoice would take {creditStatus.ContactName} ({creditStatus.ContactCode}) to " +
                $"{creditStatus.ProjectedBalance:0.##}, past their credit limit of {creditStatus.CreditLimit:0.##}. " +
                "Collect payment or raise the credit limit before approving.");
        }

        if (creditStatus.Status == CreditLimitStatus.Warn && !request.OverrideCreditLimitWarning)
        {
            throw new CreditLimitWarningException(
                $"This invoice takes {creditStatus.ContactName} ({creditStatus.ContactCode}) to " +
                $"{creditStatus.ProjectedBalance:0.##}, past their credit limit of {creditStatus.CreditLimit:0.##}. " +
                "Approve again to continue anyway.");
        }

        // Phase 28 (FR-2.5): the fold. The document stores its amounts in its own currency; the
        // general ledger is denominated in the base currency, so every line amount is converted
        // here, before the posting rule runs. Doing it here rather than on the finished GlLineInput
        // list is what keeps the entry balanced by construction -- the rule derives its balancing
        // leg as a sum of these very numbers. See ExchangeRates' doc comment.
        var postingInput = await InvoiceAccountResolver.ResolveAsync(
            db, request.OrganizationId,
            invoice.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, invoice.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, invoice.ExchangeRate))),
            resolveInventoryAccounts: goodsLines.Count > 0, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.Invoice, cancellationToken, invoice.LocationId);

        invoice.Approve(currentUser.UserId, code);

        var totalCogs = 0m;
        foreach (var line in goodsLines)
        {
            var averageUnitCost = await stockLedgerService.ConsumeAsync(
                request.OrganizationId, line.ProductId, invoice.WarehouseId, line.Quantity,
                DocumentType.Invoice, invoice.Id, invoice.Date, cancellationToken, invoice.LocationId);
            line.RecordCogsUnitCost(averageUnitCost);
            totalCogs += line.Quantity * averageUnitCost;
        }

        if (totalCogs > 0)
        {
            postingInput = postingInput with { CogsAmount = totalCogs };
        }

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(
            request.OrganizationId, DocumentType.Invoice, invoice.Id, glLines, invoice.LocationId);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveInvoiceResult(invoice.Id, invoice.Code, invoice.Status, invoice.ApprovedAt);
    }
}
