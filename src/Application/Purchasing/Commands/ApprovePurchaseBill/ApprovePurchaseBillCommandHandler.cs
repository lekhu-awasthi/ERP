using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;

/// <summary>
/// Approve() increments stock rather than decrements (Invoice's side) -- confirmed live no
/// Negative Stock Balance dialog on PurchaseBill approval (erp-module-scan.md's hands-on pass item
/// 10), so there's no availability policy to check here the way ApproveInvoiceCommandHandler
/// checks IStockAvailabilityPolicy. Phase 7: for every Goods line (a Service line never touches
/// stock -- Product.Type gate, same as Invoice's), creates a new FIFO layer. Phase 29 (FR-6.15)
/// made that layer's unit cost the line's <i>landed</i> cost -- its own net Amount plus its share
/// of the bill's Additional Cost section -- closing the scope decision phase 7 deferred. GL fix
/// post-Phase-19: Phase 6/7 originally left every
/// line -- Goods included -- debiting the Purchase (Expense) account, on the theory that a separate
/// Inventory-asset leg here would double-count "the inventory cost." That reasoning missed that
/// Invoice's own COGS relief (Phase 7) already debits COGS/credits Inventory for whatever FIFO cost
/// actually sells -- so a Goods line's Purchase-Expense debit and its eventual COGS debit were both
/// landing in Expense accounts, double-counting the same cost in IncomeStatementQueryHandler's Net
/// Profit for any tenant whose chart of accounts routes Purchase to a genuine Expense-type account
/// (confirmed the common case -- every prior phase's own manual-E2E setup named it "Purchase
/// Expense"). PurchaseBillAccountResolver now resolves a Goods line's debit account to
/// DefaultInventoryAccountId instead, making the FIFO ledger's perpetual-inventory model the actual
/// system of record: Goods purchases debit Inventory (an asset), and the only Expense recognition
/// happens once, at sale, via COGS. Service lines are untouched.
/// </summary>
public sealed class ApprovePurchaseBillCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<PurchaseBillPostingInput> postingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApprovePurchaseBillCommand, ApprovePurchaseBillResult>
{
    public async Task<ApprovePurchaseBillResult> Handle(ApprovePurchaseBillCommand request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .Include(x => x.AdditionalCosts)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Draft)
        {
            throw new ConflictException("Only a Draft purchase bill can be approved.");
        }

        if (purchaseBill.Lines.Count == 0)
        {
            throw new ConflictException("A purchase bill needs at least one line to be approved.");
        }

        // Phase 28 (FR-2.5): the fold. The document stores its amounts in its own currency; the
        // general ledger is denominated in the base currency, so every line amount is converted
        // here, before the posting rule runs. Doing it here rather than on the finished GlLineInput
        // list is what keeps the entry balanced by construction -- the rule derives its balancing
        // leg as a sum of these very numbers. See ExchangeRates' doc comment.
        var postingInput = await PurchaseBillAccountResolver.ResolveAsync(
            db, request.OrganizationId,
            purchaseBill.Lines.Select(x => (
                x.ProductId,
                ExchangeRates.ToBase(x.Amount, purchaseBill.ExchangeRate),
                ExchangeRates.ToBase(x.VatAmount, purchaseBill.ExchangeRate))),
            ExchangeRates.ToBase(purchaseBill.TdsAmount, purchaseBill.ExchangeRate), cancellationToken,
            requiresLandedCostClearing: purchaseBill.AdditionalCosts.Count > 0);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.PurchaseBill, cancellationToken);

        purchaseBill.Approve(currentUser.UserId, code);

        var productIds = purchaseBill.Lines.Select(x => x.ProductId).Distinct().ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        // Phase 29 (FR-6.15) -- spread each Additional Cost row across the goods lines it applies
        // to, in the document's own currency, before any layer is created. Goods only: see
        // PurchaseBill.AllocateAdditionalCosts for why, and why a row naming a service is rejected
        // rather than silently dropped.
        var goodsProductIds = productTypes
            .Where(x => x.Value == ProductType.Goods)
            .Select(x => x.Key)
            .ToHashSet();

        if (purchaseBill.AdditionalCosts.Count > 0)
        {
            try
            {
                // AddRange through the child DbSet rather than relying on the graph: these rows hang
                // off already-tracked parents, which EF would mark Modified instead of Added
                // (phase-24 bug #1 -- and this phase hit it, see docs/phase-29-status.md).
                db.PurchaseBillAdditionalCostAllocations.AddRange(
                    purchaseBill.AllocateAdditionalCosts(goodsProductIds));
            }
            catch (InvalidOperationException ex)
            {
                throw new ConflictException(ex.Message);
            }
        }

        // The conservation law, and the whole of this phase:
        //
        //     goods amounts (base)  +  allocated additional cost (base)
        //         =  value of the FIFO layers created  +  AdditionalCostRoundingAdjustment
        //
        // Each layer's unit cost is rounded exactly once, at the stock ledger's own scale, from the
        // line's total landed value -- so the document and the ledger can never disagree about what
        // a unit cost is (phase-25's rule). The residue that rounding leaves is named below, never
        // absorbed.
        var goodsAmountBase = 0m;
        var layerValueCreated = 0m;

        foreach (var line in purchaseBill.Lines)
        {
            if (!goodsProductIds.Contains(line.ProductId))
            {
                continue;
            }

            var allocated = purchaseBill.AllocatedAdditionalCostFor(line.Id);

            // Phase 28: FIFO layers are a base-currency store -- every later COGS posting and
            // every inventory valuation reads them without knowing which currency the bill that
            // created them was written in. So the unit cost is converted on the way in, at the
            // unit-cost scale rather than the posted-amount scale (see ExchangeRates.ToBaseUnitCost).
            // This is the one place a document's own Rate reaches the stock ledger; CreditNote and
            // the Void paths re-increment from a stored CogsUnitCost/ConsumedUnitCost, which is
            // already base currency and must not be converted a second time.
            //
            // Phase 29 changed the basis from Rate to (Amount + allocated additional cost) / Qty.
            // With neither a discount nor an additional cost the two are identical, because
            // Amount == Quantity * Rate exactly. With a discount they were not: the layer was built
            // at the undiscounted Rate while the GL debited Inventory the discounted Amount, so the
            // account and the ledger drifted apart by the discount -- a pre-existing divergence this
            // phase's conservation law does not permit and therefore closes.
            var unitCost = ExchangeRates.ToBaseUnitCost(
                (line.Amount + allocated) / line.Quantity, purchaseBill.ExchangeRate);

            goodsAmountBase += ExchangeRates.ToBase(line.Amount, purchaseBill.ExchangeRate);
            layerValueCreated += unitCost * line.Quantity;

            await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, purchaseBill.WarehouseId, line.Quantity,
                unitCost, DocumentType.PurchaseBill, purchaseBill.Id, purchaseBill.Date, cancellationToken);
        }

        if (purchaseBill.AdditionalCosts.Count > 0)
        {
            // What the layers actually received beyond the goods amounts -- the figure the GL must
            // debit Inventory for if the account is to equal the ledger, rather than the figure the
            // user typed. The difference between the two is the named residue.
            var capitalised = layerValueCreated - goodsAmountBase;
            var enteredBase = purchaseBill.AdditionalCosts
                .SelectMany(x => x.Allocations)
                .Sum(x => ExchangeRates.ToBase(x.Amount, purchaseBill.ExchangeRate));

            purchaseBill.RecordAdditionalCostCapitalisation(capitalised, enteredBase - capitalised);
            postingInput = postingInput with { CapitalisedAdditionalCost = capitalised };
        }

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.PurchaseBill, purchaseBill.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePurchaseBillResult(
            purchaseBill.Id,
            purchaseBill.Code,
            purchaseBill.Status,
            purchaseBill.ApprovedAt,
            purchaseBill.CapitalisedAdditionalCost,
            purchaseBill.AdditionalCostRoundingAdjustment);
    }
}
