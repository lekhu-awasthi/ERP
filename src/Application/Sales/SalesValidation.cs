using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales;

/// <summary>Shared existence checks reused by every Sales/Payments Create/Update handler --
/// mirrors Accounting.AccountingValidation's precedent.</summary>
internal static class SalesValidation
{
    public static async Task EnsureContactExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, ContactType expectedType, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == expectedType, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException($"{expectedType} not found.");
        }
    }

    public static async Task EnsureProductsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var existingCount = await db.Products.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more products were not found.");
        }
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

    /// <summary>Remaining-to-credit quantity on an Invoice, net of every existing non-Void
    /// CreditNote already referencing it, keyed by the exact (ProductId, Rate, VatRate) triple a
    /// line was actually sold at -- not ProductId alone. A Credit Note can only reduce a
    /// quantity that was genuinely invoiced at that same Rate and VatRate; it can't invent a
    /// cheaper/pricier or differently-taxed line for a product that really was on the invoice
    /// (that would misstate both revenue and the VAT liability the reversal is supposed to
    /// exactly undo -- see docs/phase-6-status.md's DebitNote/TDS reversal-imbalance bug for why
    /// "the reversal's own entry balances" isn't enough proof of correctness).</summary>
    public static async Task<Dictionary<(Guid ProductId, decimal Rate, VatRate VatRate), decimal>> GetInvoiceRemainingByLineAsync(
        IAppDbContext db, Guid organizationId, Invoice invoice, CancellationToken cancellationToken)
    {
        var creditedLines = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId
                && x.ReferrerType == DocumentType.Invoice && x.ReferrerId == invoice.Id
                && x.Status != CreditNoteStatus.Void)
            .SelectMany(x => x.Lines)
            .Select(x => new { x.ProductId, x.Rate, x.VatRate, x.Quantity })
            .ToListAsync(cancellationToken);

        var creditedByLine = creditedLines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        return invoice.Lines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity) - creditedByLine.GetValueOrDefault(g.Key));
    }

    /// <summary>Guards CreateCreditNoteCommandHandler against returning more than what's actually
    /// remaining on the source Invoice, crediting a product/rate/VAT combination that was never
    /// actually invoiced, or crediting a different Customer than the one who was actually
    /// invoiced -- see docs/phase-5-status.md/phase-6-status.md's write-up of this gap for why it
    /// wasn't caught earlier (standalone Credit Notes with no ReferrerId are intentionally left
    /// unchecked; there's no source to cap against).</summary>
    public static async Task EnsureCreditNoteLinesWithinInvoiceRemainingAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid invoiceId,
        Guid contactId,
        IReadOnlyList<CreditNoteLineInput> requestedLines,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.ContactId != contactId)
        {
            throw new ConflictException("A credit note converted from an Invoice must keep the same Customer.");
        }

        var remainingByLine = await GetInvoiceRemainingByLineAsync(db, organizationId, invoice, cancellationToken);

        var requestedByLine = requestedLines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var (key, requestedQuantity) in requestedByLine)
        {
            if (!remainingByLine.TryGetValue(key, out var remaining))
            {
                throw new ConflictException(
                    "This credit note contains a product/rate/VAT combination that doesn't match any line on the source invoice.");
            }

            if (requestedQuantity > remaining)
            {
                throw new ConflictException(
                    $"Cannot credit {requestedQuantity} of this line -- only {remaining} remains un-credited on the source invoice.");
            }
        }
    }
}
