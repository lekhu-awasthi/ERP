using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Shared by ApprovePurchaseBillCommandHandler and PreviewPurchaseBillGlPostingQuery -- resolves
/// each line's debit account plus the tenant's Accounts Payable/VAT Receivable/TDS Payable
/// accounts, then hands back the pure PurchaseBillPostingInput PurchaseBillPostingRule.BuildLines
/// consumes. Same friendly-ConflictException-not-a-Domain-500 precedent as
/// Sales.Posting.InvoiceAccountResolver.
///
/// A Goods line always debits TenantSettings.DefaultInventoryAccountId (Product.PurchaseAccountId
/// is ignored for Goods -- Phase 7 never added a per-Product inventory-account override, tenant-wide
/// only, same precedent as Sales' Inventory/COGS defaults). A Service line keeps the original Phase
/// 6 behaviour: Product.PurchaseAccountId falling back to TenantSettings.DefaultPurchaseAccountId.
/// Fixed post-Phase-19: the original design debited every line's Purchase (Expense) account
/// regardless of Product.Type, which double-posted the cost of Goods lines once as a period expense
/// here and again as COGS on the eventual Invoice's FIFO relief -- see the investigation this fix
/// closes for the full reasoning (erp-module-scan.md's own reference-product formula, "Cost Of Sales
/// = Opening Inventory + Purchases - Closing Inventory", requires Purchases to actually reach the
/// Inventory account, which they never did before this fix). Goods lines now behave as a genuine
/// perpetual-inventory purchase (Debit Inventory, not Debit Expense); COGS is recognised only once,
/// at sale, via the existing Phase 7 FIFO relief. Service lines are untouched -- they were never
/// double-counted since they never touch the FIFO ledger.
/// </summary>
internal static class PurchaseBillAccountResolver
{
    public static async Task<PurchaseBillPostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        decimal tdsAmount,
        CancellationToken cancellationToken,
        bool requiresLandedCostClearing = false)
    {
        var lineList = lines.ToList();
        var productIds = lineList.Select(x => x.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PurchaseAccountId, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x, cancellationToken);

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var postingLines = new List<PurchaseBillPostingLineInput>();
        foreach (var line in lineList)
        {
            products.TryGetValue(line.ProductId, out var product);

            Guid debitAccountId;
            if (product?.Type == ProductType.Goods)
            {
                debitAccountId = settings.DefaultInventoryAccountId
                    ?? throw new ConflictException(
                        "Default Inventory account is not configured. Set it under Accounting Defaults before approving purchase bills with Goods lines.");
            }
            else
            {
                debitAccountId = product?.PurchaseAccountId
                    ?? settings.DefaultPurchaseAccountId
                    ?? throw new ConflictException(
                        "One or more products have no Purchase Account and no Default Purchase Account is configured. " +
                        "Set a Purchase Account on the product, or configure a Default Purchase Account under Accounting Defaults.");
            }

            postingLines.Add(new PurchaseBillPostingLineInput(debitAccountId, line.Amount, line.VatAmount));
        }

        if (settings.DefaultAccountsPayableId is not { } accountsPayableId)
        {
            throw new ConflictException(
                "Default Accounts Payable account is not configured. Set it under Accounting Defaults before approving purchase bills.");
        }

        var totalVat = lineList.Sum(x => x.VatAmount);
        if (totalVat > 0 && settings.DefaultVatReceivableAccountId is null)
        {
            throw new ConflictException(
                "Default VAT Receivable account is not configured. Set it under Accounting Defaults before approving purchase bills with VAT.");
        }

        if (tdsAmount > 0 && settings.DefaultTdsPayableAccountId is null)
        {
            throw new ConflictException(
                "Default TDS Payable account is not configured. Set it under Accounting Defaults before approving purchase bills with TDS.");
        }

        // Phase 29 (FR-6.15). Demanded only when the bill actually carries an Additional Cost
        // section -- the same lazy treatment phase 28 gave the two forex accounts, so a tenant that
        // never uses landed cost never has to configure an account for it. Checked here, before
        // Approve creates a single FIFO layer, so a missing account is a clean 409 rather than a
        // half-applied approval.
        Guid? landedCostClearingAccountId = null;
        if (requiresLandedCostClearing)
        {
            landedCostClearingAccountId = settings.DefaultLandedCostClearingAccountId
                ?? throw new ConflictException(
                    "Default Landed Cost Clearing account is not configured. Set it under Accounting Defaults "
                    + "before approving purchase bills that carry an Additional Cost.");

            if (settings.DefaultInventoryAccountId is null)
            {
                throw new ConflictException(
                    "Default Inventory account is not configured. Set it under Accounting Defaults before "
                    + "approving purchase bills that carry an Additional Cost.");
            }
        }

        return new PurchaseBillPostingInput(
            accountsPayableId,
            settings.DefaultVatReceivableAccountId ?? Guid.Empty,
            settings.DefaultTdsPayableAccountId ?? Guid.Empty,
            tdsAmount,
            postingLines,
            settings.DefaultInventoryAccountId,
            landedCostClearingAccountId);
    }
}
