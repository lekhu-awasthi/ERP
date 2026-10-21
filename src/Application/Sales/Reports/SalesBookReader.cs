using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Reports;

/// <summary>
/// Phase 89 -- the sales book's positive half: every approved Invoice in a period with the four statutory magnitudes
/// (total, tax-exempt, taxable, VAT) and its export block. Extracted from <c>SalesRegisterQueryHandler</c> so the Sales
/// Register and the VAT return read the same invoices through the same fold and the same split; phase 26c's
/// <see cref="SalesReturnReader"/> already does this for the credit notes. A month of the return equals the register run
/// over that month by construction, and <c>VatReturnRegisterAgreementTests</c> holds them to it.
///
/// <para>The split is the register's own rule, unchanged: a line with no VAT is tax-exempt, a line with VAT is taxable.
/// An export invoice's lines are zero-rated (<c>Invoice.AddLine</c> enforces it), so its whole value lands in TaxExempt
/// <i>and</i> is reported again as the invoice's export value; the VAT return's "local exempt" line is therefore the
/// tax-exempt value of the invoices that are not exports.</para>
/// </summary>
internal static class SalesBookReader
{
    /// <summary>One line in base currency. Carried for the Sales Register's Group By Bill toggle (phase 36), so the per-line
    /// view and the per-document view split the same folded amounts.</summary>
    internal sealed record InvoiceLineRow(Guid ProductId, decimal Quantity, decimal Amount, decimal VatAmount, VatRate VatRate);

    internal sealed record InvoiceRow(
        Guid Id,
        Guid ContactId,
        string Code,
        DateOnly Date,
        bool IsExport,
        string? ExportCountry,
        string? ExportDeclarationNo,
        DateOnly? ExportDeclarationDate,
        SalesReturnReader.Bucketed Buckets,
        IReadOnlyList<InvoiceLineRow> Lines)
    {
        /// <summary>The register's Export Value column: the invoice's own total when it is an export, else nothing.</summary>
        public decimal ExportValue => IsExport ? Buckets.Total : 0;
    }

    /// <param name="onlyInvoiceIds">The Sales Register's reporting-tag narrowing; null is every invoice.</param>
    internal static async Task<List<InvoiceRow>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? contactId,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null,
        SalesChannel? channel = null,
        IReadOnlyCollection<Guid>? onlyInvoiceIds = null)
    {
        var invoiceQuery = db.Invoices.Where(x =>
            x.OrganizationId == organizationId && x.Status == InvoiceStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        if (contactId is { } invoiceContactId)
        {
            invoiceQuery = invoiceQuery.Where(x => x.ContactId == invoiceContactId);
        }
        if (onlyInvoiceIds is not null)
        {
            invoiceQuery = invoiceQuery.Where(x => onlyInvoiceIds.Contains(x.Id));
        }

        // Phase 44 -- the Billing Location filter and the caller's report scope (phase 35b).
        invoiceQuery = invoiceQuery.AtLocations(locationId, reportLocations);

        // Phase 66 -- the sales channel, so a register read with Channel = Pos narrows this half as SalesReturnReader
        // narrows the other.
        if (channel is { } onlyChannel)
        {
            invoiceQuery = invoiceQuery.Where(x => x.Channel == onlyChannel);
        }

        var invoices = await invoiceQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.Code, x.Date,
                x.IsExport, x.ExportCountry, x.ExportDeclarationNo, x.ExportDeclarationDate,
            })
            .ToListAsync(cancellationToken);

        // Phase 42 -- the lines are joined to the invoice *query*, not fetched for a list of ids read back out of
        // `invoices`. That list is one id per invoice in the period -- 50,000 over three years on the scale dataset -- and
        // would reach SQL Server as an OPENJSON parameter of the same length (phase-34c's gotcha at full size).
        var rawLines = await (
                from line in db.InvoiceLines
                join invoice in invoiceQuery on line.InvoiceId equals invoice.Id
                select new
                {
                    // Phase 61 -- a till line's service charge is inside the VAT base, so the book counts it with the line
                    // it was charged on (zero on every ERP line), and phase 70's packaging fee likewise. The round-off is
                    // not a supply, so no row carries it: Total stays Exempt + Taxable + VAT.
                    line.InvoiceId, line.ProductId, line.Quantity,
                    Amount = line.Amount + line.ServiceChargeAmount + line.PackagingFeeAmount,
                    line.VatAmount, line.VatRate, invoice.ExchangeRate,
                })
            .ToListAsync(cancellationToken);

        // Phase 43 (36 carried item #5) -- <b>the book is filed in NPR, so it reports NPR.</b> Folded per line, before the
        // split, never on the finished buckets: Total is a sum of the other three, so converting the buckets
        // independently would let it differ from TaxExempt + Taxable + VAT by a paisa. In memory, after ToListAsync,
        // because ExchangeRates.ToBase is a static call SQL Server cannot translate and InMemory silently evaluates in C#
        // (phase-34b, phase-25).
        var linesByInvoice = rawLines
            .GroupBy(x => x.InvoiceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new InvoiceLineRow(
                        x.ProductId,
                        x.Quantity,
                        ExchangeRates.ToBase(x.Amount, x.ExchangeRate),
                        ExchangeRates.ToBase(x.VatAmount, x.ExchangeRate),
                        x.VatRate))
                    .ToList());

        return
        [
            .. invoices.Select(x =>
            {
                var lines = linesByInvoice.GetValueOrDefault(x.Id) ?? [];
                return new InvoiceRow(
                    x.Id, x.ContactId, x.Code, x.Date,
                    x.IsExport, x.ExportCountry, x.ExportDeclarationNo, x.ExportDeclarationDate,
                    Split(lines),
                    lines);
            }),
        ];
    }

    /// <summary>The register's four magnitudes over a set of lines: a zero-VAT line is tax-exempt, any other is taxable.
    /// Shared by a whole invoice and a single line, so the per-line view sums to the per-document one.</summary>
    internal static SalesReturnReader.Bucketed Split(IEnumerable<InvoiceLineRow> lines)
    {
        decimal total = 0, taxExempt = 0, taxable = 0, vat = 0, zeroRated = 0;
        foreach (var line in lines)
        {
            total += line.Amount + line.VatAmount;
            vat += line.VatAmount;
            if (line.VatAmount == 0)
            {
                taxExempt += line.Amount;
                if (line.VatRate == VatRate.ZeroVat)
                {
                    zeroRated += line.Amount;
                }
            }
            else
            {
                taxable += line.Amount;
            }
        }

        return new SalesReturnReader.Bucketed(total, taxExempt, taxable, vat, zeroRated);
    }
}
