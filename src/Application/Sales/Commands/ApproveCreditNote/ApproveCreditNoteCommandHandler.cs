using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveCreditNote;

/// <summary>
/// Post-Phase-7 fix: a CreditNote whose ReferrerType/ReferrerId point at the Invoice it reverses
/// now puts stock back, instead of leaving the FIFO ledger untouched (phase-7-status.md's flagged
/// gap). For each of this CreditNote's Goods lines, finds the matching source InvoiceLine(s) by the
/// same exact (ProductId, Rate, VatRate) triple SalesValidation already caps quantities against, and
/// calls IStockLedgerService.IncrementAsync at the Invoice's own WarehouseId, using the
/// quantity-weighted average of those lines' InvoiceLine.CogsUnitCost (recorded when the Invoice was
/// originally approved -- see InvoiceLine's doc comment) -- not a freshly-guessed cost, so a return
/// re-enters stock at what it actually left at. A standalone CreditNote (no ReferrerId, or one whose
/// referrer isn't an Invoice) skips this entirely -- there is no source to know a WarehouseId or
/// cost from, same as CreateCreditNoteCommandHandler already treats it for quantity-capping.
/// </summary>
public sealed class ApproveCreditNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<CreditNotePostingInput> postingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApproveCreditNoteCommand, ApproveCreditNoteResult>
{
    public async Task<ApproveCreditNoteResult> Handle(ApproveCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        if (creditNote.Status != CreditNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft credit note can be approved.");
        }

        if (creditNote.Lines.Count == 0)
        {
            throw new ConflictException("A credit note needs at least one line to be approved.");
        }

        var productIds = creditNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var goodsLines = creditNote.Lines.Where(x => productTypes.GetValueOrDefault(x.ProductId) == ProductType.Goods).ToList();

        Invoice? sourceInvoice = null;
        if (creditNote.ReferrerType == DocumentType.Invoice && creditNote.ReferrerId is { } invoiceId && goodsLines.Count > 0)
        {
            sourceInvoice = await db.Invoices
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == request.OrganizationId, cancellationToken);
        }

        // Phase 28 (FR-2.5): the fold. The document stores its amounts in its own currency; the
        // general ledger is denominated in the base currency, so every line amount is converted
        // here, before the posting rule runs. Doing it here rather than on the finished GlLineInput
        // list is what keeps the entry balanced by construction -- the rule derives its balancing
        // leg as a sum of these very numbers. See ExchangeRates' doc comment.
        var postingInput = await CreditNoteAccountResolver.ResolveAsync(
            db, request.OrganizationId,
            creditNote.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, creditNote.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, creditNote.ExchangeRate))),
            resolveInventoryAccounts: sourceInvoice is not null, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.CreditNote, cancellationToken);

        creditNote.Approve(currentUser.UserId, code);

        var totalCogsReversal = 0m;
        if (sourceInvoice is not null)
        {
            var costByLine = sourceInvoice.Lines
                .Where(x => x.CogsUnitCost is not null)
                .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity * x.CogsUnitCost!.Value) / g.Sum(x => x.Quantity));

            foreach (var line in goodsLines)
            {
                if (!costByLine.TryGetValue((line.ProductId, line.Rate, line.VatRate), out var unitCost))
                {
                    continue;
                }

                await stockLedgerService.IncrementAsync(
                    request.OrganizationId, line.ProductId, sourceInvoice.WarehouseId, line.Quantity, unitCost,
                    DocumentType.CreditNote, creditNote.Id, creditNote.Date, cancellationToken);
                totalCogsReversal += line.Quantity * unitCost;
            }
        }

        if (totalCogsReversal > 0)
        {
            postingInput = postingInput with { CogsAmount = totalCogsReversal };
        }

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.CreditNote, creditNote.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status, creditNote.ApprovedAt);
    }
}
