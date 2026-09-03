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

    internal sealed record CreditNoteRow(Guid Id, Guid ContactId, string Code, DateOnly Date, Bucketed Buckets);

    internal static async Task<List<CreditNoteRow>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? contactId,
        CancellationToken cancellationToken)
    {
        var query = db.CreditNotes.Where(x =>
            x.OrganizationId == organizationId && x.Status == CreditNoteStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        if (contactId is { } filter)
        {
            query = query.Where(x => x.ContactId == filter);
        }

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
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var buckets = lines
            .GroupBy(x => x.CreditNoteId)
            .ToDictionary(g => g.Key, g => new Bucketed(
                Total: g.Sum(x => x.Amount + x.VatAmount),
                TaxExempt: g.Where(x => x.VatAmount == 0).Sum(x => x.Amount),
                Taxable: g.Where(x => x.VatAmount != 0).Sum(x => x.Amount),
                Vat: g.Sum(x => x.VatAmount)));

        return [.. creditNotes.Select(x => new CreditNoteRow(
            x.Id, x.ContactId, x.Code, x.Date, buckets.GetValueOrDefault(x.Id) ?? Bucketed.Empty))];
    }
}
