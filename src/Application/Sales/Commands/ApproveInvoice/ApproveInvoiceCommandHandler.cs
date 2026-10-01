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
using ErpApp.Domain.Inventory;
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
        // Phase 36 -- the document's own total folded into the base currency, because that is the
        // unit the contact's balance and their credit limit are both in (phase-31 carried item #3).
        // A base-currency invoice converts at rate 1, so nothing changes for a single-currency
        // tenant.
        var creditStatus = await creditLimitPolicy.CheckAsync(
            request.OrganizationId, invoice.ContactId,
            ExchangeRates.ToBase(invoice.GrandTotal, invoice.ExchangeRate), cancellationToken);

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

        // Phase 51 -- the serials each line names, loaded once for the whole document. A line of a
        // non-serialised product (or a Service line) is simply absent from the dictionary.
        var serialsByLine = await LineStockAllocator.LoadSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.InvoiceLine,
            invoice.Lines.Select(x => x.Id).ToList(), cancellationToken);

        // Phase 61 -- the number, the status, the stock and the sale entry, through the one path the
        // till's create-approved sale takes too (see InvoiceApprovalPosting for what stays here).
        await InvoiceApprovalPosting.ApproveAndPostAsync(
            db, numberGenerator, postingRule, stockLedgerService, currentUser.UserId, invoice, serialsByLine,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveInvoiceResult(invoice.Id, invoice.Code, invoice.Status, invoice.ApprovedAt);
    }
}
