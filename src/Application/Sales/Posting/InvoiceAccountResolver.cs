using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Shared by ApproveInvoiceCommandHandler and the Invoice PreviewGlPostingQuery handler -- resolves
/// each line's Sales Account (Product.SalesAccountId, falling back to TenantSettings'
/// DefaultSalesAccountId) plus the tenant's Accounts Receivable/VAT Payable accounts, then hands
/// back the pure InvoicePostingInput InvoicePostingRule.BuildLines consumes. Throws a friendly
/// ConflictException (not the Domain-level InvalidOperationException/500) the moment a required
/// account is missing -- see phase-5-status.md's scope decision on the TenantSettings fallback.
///
/// resolveInventoryAccounts (Phase 7) gates whether the Inventory/COGS default accounts are also
/// resolved and validated: ApproveInvoiceCommandHandler passes true only when the invoice has at
/// least one Goods line (so an all-Service invoice, or a tenant that hasn't set the new Inventory
/// defaults yet but only ever invoices Services, never fails on a check it doesn't need). The
/// GL-preview-before-approve query always passes false -- it deliberately never estimates a COGS
/// leg, since doing so accurately would need a WarehouseId the preview endpoint doesn't carry (see
/// phase-7-status.md's scope decision). CogsAmount itself is never set here -- the real FIFO cost
/// isn't known until ApproveInvoiceCommandHandler calls IStockLedgerService.ConsumeAsync, after
/// this resolver has already run.
/// </summary>
internal static class InvoiceAccountResolver
{
    public static async Task<InvoicePostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        bool resolveInventoryAccounts,
        CancellationToken cancellationToken)
    {
        var lineList = lines.ToList();
        var productIds = lineList.Select(x => x.ProductId).Distinct().ToList();

        var productSalesAccounts = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.SalesAccountId })
            .ToDictionaryAsync(x => x.Id, x => x.SalesAccountId, cancellationToken);

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var postingLines = new List<InvoicePostingLineInput>();
        foreach (var line in lineList)
        {
            var salesAccountId = (productSalesAccounts.TryGetValue(line.ProductId, out var productAccountId) ? productAccountId : null)
                ?? settings.DefaultSalesAccountId
                ?? throw new ConflictException(
                    "One or more products have no Sales Account and no Default Sales Account is configured. " +
                    "Set a Sales Account on the product, or configure a Default Sales Account under Accounting Defaults.");

            postingLines.Add(new InvoicePostingLineInput(salesAccountId, line.Amount, line.VatAmount));
        }

        if (settings.DefaultAccountsReceivableId is not { } accountsReceivableId)
        {
            throw new ConflictException(
                "Default Accounts Receivable account is not configured. Set it under Accounting Defaults before approving invoices.");
        }

        var totalVat = lineList.Sum(x => x.VatAmount);
        if (totalVat > 0 && settings.DefaultVatPayableAccountId is null)
        {
            throw new ConflictException(
                "Default VAT Payable account is not configured. Set it under Accounting Defaults before approving invoices with VAT.");
        }

        Guid? cogsAccountId = null;
        Guid? inventoryAccountId = null;
        if (resolveInventoryAccounts)
        {
            cogsAccountId = settings.DefaultCogsAccountId
                ?? throw new ConflictException(
                    "Default COGS account is not configured. Set it under Accounting Defaults before approving invoices with Goods lines.");
            inventoryAccountId = settings.DefaultInventoryAccountId
                ?? throw new ConflictException(
                    "Default Inventory account is not configured. Set it under Accounting Defaults before approving invoices with Goods lines.");
        }

        return new InvoicePostingInput(
            accountsReceivableId, settings.DefaultVatPayableAccountId ?? Guid.Empty, postingLines, cogsAccountId, inventoryAccountId);
    }
}
