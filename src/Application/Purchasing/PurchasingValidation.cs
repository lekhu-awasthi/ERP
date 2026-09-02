using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing;

/// <summary>Shared existence checks reused by every Purchasing Create/Update handler -- mirrors
/// Sales.SalesValidation's precedent, Contact type filtered to Supplier instead of Customer.</summary>
internal static class PurchasingValidation
{
    public static async Task EnsureSupplierExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == ContactType.Supplier, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Supplier not found.");
        }
    }

    public static async Task EnsureProductsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        // Phase 24: also refuses a variant *parent*. See ProductVariantRules -- this helper is one
        // of the four call sites that make up the whole sweep.
        await ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
            db, organizationId, productIds, cancellationToken);
    }

    public static async Task EnsureWarehouseExistsAsync(
        IAppDbContext db, Guid organizationId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var exists = await db.Warehouses.AnyAsync(
            x => x.Id == warehouseId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }

    public static async Task<decimal> ResolveTdsAmountAsync(
        IAppDbContext db, Guid organizationId, Guid? tdsTypeId, decimal tdsBaseAmount, CancellationToken cancellationToken)
    {
        if (tdsTypeId is not { } id)
        {
            return 0;
        }

        var tdsType = await db.TdsTypes.SingleOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("TDS type not found.");

        return Math.Round(tdsBaseAmount * tdsType.RatePct / 100m, 4);
    }

    /// <summary>Mirror of Sales.SalesValidation.GetInvoiceRemainingByLineAsync -- see that
    /// method's doc comment for why matching is keyed on the exact (ProductId, Rate, VatRate,
    /// DiscountPct) quadruple a line was actually billed at, not ProductId alone.</summary>
    public static async Task<Dictionary<(Guid ProductId, decimal Rate, VatRate VatRate, decimal DiscountPct), decimal>>
        GetPurchaseBillRemainingByLineAsync(
            IAppDbContext db, Guid organizationId, PurchaseBill purchaseBill, CancellationToken cancellationToken)
    {
        var debitedLines = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId
                && x.ReferrerType == DocumentType.PurchaseBill && x.ReferrerId == purchaseBill.Id
                && x.Status != DebitNoteStatus.Void)
            .SelectMany(x => x.Lines)
            .Select(x => new { x.ProductId, x.Rate, x.VatRate, x.DiscountPct, x.Quantity })
            .ToListAsync(cancellationToken);

        var debitedByLine = debitedLines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate, x.DiscountPct))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        return purchaseBill.Lines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate, x.DiscountPct))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity) - debitedByLine.GetValueOrDefault(g.Key));
    }

    /// <summary>Mirror of Sales.SalesValidation.EnsureCreditNoteLinesWithinInvoiceRemainingAsync
    /// -- guards CreateDebitNoteCommandHandler the same way, against the source PurchaseBill's
    /// own remaining quantity per exact (ProductId, Rate, VatRate, DiscountPct) line, plus
    /// Supplier, TDS Type, and transaction-level DiscountPct matching the source exactly (a
    /// different TdsTypeId would reverse withholding at the wrong rate, and a different header
    /// DiscountPct would debit an Amount that doesn't match what was actually billed even if every
    /// per-line key matches -- both leave a paired account permanently unbalanced across the pair,
    /// the same failure mode as docs/phase-6-status.md's bug #3, just reached via a different
    /// field).</summary>
    public static async Task EnsureDebitNoteLinesWithinPurchaseBillRemainingAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid purchaseBillId,
        Guid contactId,
        Guid? tdsTypeId,
        decimal discountPct,
        IReadOnlyList<DebitNoteLineInput> requestedLines,
        CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == purchaseBillId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.ContactId != contactId)
        {
            throw new ConflictException("A debit note converted from a Purchase Bill must keep the same Supplier.");
        }

        if (purchaseBill.TdsTypeId != tdsTypeId)
        {
            throw new ConflictException("A debit note converted from a Purchase Bill must keep the same TDS Type.");
        }

        if (purchaseBill.DiscountPct != discountPct)
        {
            throw new ConflictException(
                "A debit note converted from a Purchase Bill must keep the same transaction-level Discount% as the source purchase bill.");
        }

        var remainingByLine = await GetPurchaseBillRemainingByLineAsync(db, organizationId, purchaseBill, cancellationToken);

        var requestedByLine = requestedLines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate, x.DiscountPct))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var (key, requestedQuantity) in requestedByLine)
        {
            if (!remainingByLine.TryGetValue(key, out var remaining))
            {
                throw new ConflictException(
                    "This debit note contains a product/rate/VAT/discount combination that doesn't match any line on the source purchase bill.");
            }

            if (requestedQuantity > remaining)
            {
                throw new ConflictException(
                    $"Cannot debit {requestedQuantity} of this line -- only {remaining} remains un-debited on the source purchase bill.");
            }
        }
    }
}
