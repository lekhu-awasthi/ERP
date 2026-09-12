using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Reports;

/// <summary>
/// Every approved Credit Note in a period, split into the four statutory magnitudes the Nepal IRD
/// sales books use: total, tax-exempt, taxable value and tax. Phase 26c's sales-side counterpart to
/// <c>PurchaseReturnReader</c>, and it exists for the same reason -- the <b>Sales Register</b> and
/// the new <b>Sales Return Register</b> show the same credit notes (negative in the first, positive
/// in the second, confirmed side by side on the live tenant on 2026-09-03), so one reader produces
/// the magnitudes and neither report can drift from the other.
///
/// <para>The split is the register's own long-standing rule, unchanged: a line with no VAT is
/// tax-exempt, a line with VAT is taxable, and the tax column is the VAT itself.</para>
/// </summary>
internal static class SalesReturnReader
{
    internal sealed record Bucketed(decimal Total, decimal TaxExempt, decimal Taxable, decimal Vat)
    {
        internal static Bucketed Empty { get; } = new(0, 0, 0, 0);
    }

    internal sealed record CreditNoteRow(
        Guid Id, Guid ContactId, string Code, DateOnly Date, Bucketed Buckets,
        IReadOnlyList<CreditNoteLineRow>? Lines = null);

    /// <summary>
    /// Phase 36 -- one returned line, for the Sales Register's <b>Group By Bill</b> toggle. Turning
    /// it off makes the register render a row per line with the item's name, quantity and unit
    /// beside the same four statutory magnitudes (confirmed live on Moonbeam 2026-09-11: 27 rows
    /// became 50, three columns appeared, and the footer total did not move).
    ///
    /// <para>Loaded here rather than in the register's own handler so the two views split the same
    /// bucketing: a line's buckets sum to its note's buckets by construction, which is what keeps
    /// the grouped and ungrouped totals identical.</para>
    /// </summary>
    internal sealed record CreditNoteLineRow(string ItemName, decimal Quantity, string Unit, Bucketed Buckets);

    internal static async Task<List<CreditNoteRow>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? contactId,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null)
    {
        var query = db.CreditNotes.Where(x =>
            x.OrganizationId == organizationId && x.Status == CreditNoteStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        if (contactId is { } filter)
        {
            query = query.Where(x => x.ContactId == filter);
        }

        // Phase 35b -- the Billing Location filter and the caller's report scope. Applied to the
        // CreditNote itself: it carries its own LocationId, so unlike its warehouse there is no
        // referrer lookup to fall back on.
        query = query.AtLocations(locationId, reportLocations);

        var creditNotes = await query
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date })
            .ToListAsync(cancellationToken);
        if (creditNotes.Count == 0)
        {
            return [];
        }

        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();
        var lines = await db.CreditNoteLines
            .Where(x => creditNoteIds.Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.ProductId, x.Quantity, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        // Phase 36 -- the item's own name and unit, for the per-line view. One pair of lookups for
        // the whole period, not one per line.
        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.PrimaryUnitId })
            .ToListAsync(cancellationToken);
        var unitIds = products.Select(x => x.PrimaryUnitId).Distinct().ToList();
        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == organizationId && unitIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var productLookup = products.ToDictionary(
            x => x.Id, x => (x.Name, Unit: unitNames.GetValueOrDefault(x.PrimaryUnitId, string.Empty)));

        var lineRows = lines
            .GroupBy(x => x.CreditNoteId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditNoteLineRow>)
                [
                    .. g.Select(line =>
                    {
                        var product = productLookup.GetValueOrDefault(line.ProductId);
                        return new CreditNoteLineRow(
                            product.Name ?? string.Empty,
                            line.Quantity,
                            product.Unit ?? string.Empty,
                            new Bucketed(
                                Total: line.Amount + line.VatAmount,
                                TaxExempt: line.VatAmount == 0 ? line.Amount : 0,
                                Taxable: line.VatAmount != 0 ? line.Amount : 0,
                                Vat: line.VatAmount));
                    }),
                ]);

        var buckets = lines
            .GroupBy(x => x.CreditNoteId)
            .ToDictionary(g => g.Key, g => new Bucketed(
                Total: g.Sum(x => x.Amount + x.VatAmount),
                TaxExempt: g.Where(x => x.VatAmount == 0).Sum(x => x.Amount),
                Taxable: g.Where(x => x.VatAmount != 0).Sum(x => x.Amount),
                Vat: g.Sum(x => x.VatAmount)));

        return [.. creditNotes.Select(x => new CreditNoteRow(
            x.Id, x.ContactId, x.Code, x.Date, buckets.GetValueOrDefault(x.Id) ?? Bucketed.Empty,
            lineRows.GetValueOrDefault(x.Id)))];
    }
}
