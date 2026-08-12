using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Shared by ApprovePurchaseBillCommandHandler and PreviewPurchaseBillGlPostingQuery -- resolves
/// each line's Purchase Account (Product.PurchaseAccountId, falling back to TenantSettings'
/// DefaultPurchaseAccountId) plus the tenant's Accounts Payable/VAT Receivable/TDS Payable
/// accounts, then hands back the pure PurchaseBillPostingInput PurchaseBillPostingRule.BuildLines
/// consumes. Same friendly-ConflictException-not-a-Domain-500 precedent as
/// Sales.Posting.InvoiceAccountResolver.
/// </summary>
internal static class PurchaseBillAccountResolver
{
    public static async Task<PurchaseBillPostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        decimal tdsAmount,
        CancellationToken cancellationToken)
    {
        var lineList = lines.ToList();
        var productIds = lineList.Select(x => x.ProductId).Distinct().ToList();

        var productPurchaseAccounts = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PurchaseAccountId })
            .ToDictionaryAsync(x => x.Id, x => x.PurchaseAccountId, cancellationToken);

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var postingLines = new List<PurchaseBillPostingLineInput>();
        foreach (var line in lineList)
        {
            var purchaseAccountId = (productPurchaseAccounts.TryGetValue(line.ProductId, out var productAccountId) ? productAccountId : null)
                ?? settings.DefaultPurchaseAccountId
                ?? throw new ConflictException(
                    "One or more products have no Purchase Account and no Default Purchase Account is configured. " +
                    "Set a Purchase Account on the product, or configure a Default Purchase Account under Accounting Defaults.");

            postingLines.Add(new PurchaseBillPostingLineInput(purchaseAccountId, line.Amount, line.VatAmount));
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

        return new PurchaseBillPostingInput(
            accountsPayableId,
            settings.DefaultVatReceivableAccountId ?? Guid.Empty,
            settings.DefaultTdsPayableAccountId ?? Guid.Empty,
            tdsAmount,
            postingLines);
    }
}
