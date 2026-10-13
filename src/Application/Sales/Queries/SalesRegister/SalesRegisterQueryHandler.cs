using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration;
using ErpApp.Application.Sales.Reports;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesRegister;

public sealed class SalesRegisterQueryHandler(IAppDbContext db, ICurrentUserService currentUser) : IRequestHandler<SalesRegisterQuery, SalesRegisterDto>
{
    public async Task<SalesRegisterDto> Handle(SalesRegisterQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var taggedInvoiceIds = await ReportingTagFilter.ResolveMatchingDocumentIdsAsync(
            db, DocumentType.Invoice, request.TagOptionIds, cancellationToken);
        var tagFilterActive = request.TagOptionIds is { Count: > 0 };

        var rows = new List<SalesRegisterRowDto>();

        if (!tagFilterActive || taggedInvoiceIds is { Count: > 0 })
        {
            // Phase 89 -- the invoice half comes from SalesBookReader, which the VAT return also reads, so a month of the
            // return equals this register over that month by construction. The reader carries every filter this half
            // applied (contact, tags, phase 44's location, phase 66's channel), phase 61/70's fees inside the VAT base,
            // phase 43's fold to base currency, and phase 42's join to the invoice query rather than a list of ids.
            var invoices = await SalesBookReader.LoadAsync(
                db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
                request.LocationId, reportLocations, request.Channel, taggedInvoiceIds);

            // Phase 36 -- the item's name and unit, needed only when Group By Bill is off. Loaded once for the period
            // either way rather than branching two data paths.
            //
            // Phase 42 -- and loaded for the tenant rather than for the distinct product ids the period's lines mention:
            // 20,000 ids sent back is dearer than the index scan that returns 20,000 rows. The dictionaries are lookups, so
            // extra entries change nothing.
            var lineProducts = await db.Products
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Id, x.Name, x.PrimaryUnitId })
                .ToListAsync(cancellationToken);
            var lineUnits = await db.UnitsOfMeasurement
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
            var lineProductLookup = lineProducts.ToDictionary(
                x => x.Id, x => (x.Name, Unit: lineUnits.GetValueOrDefault(x.PrimaryUnitId, string.Empty)));

            // Phase 42 -- the tenant's contacts, not the 35,001 ids the period's invoices name. This read alone measured
            // 155,435 logical reads and 582 ms as an OPENJSON join.
            var invoiceContacts = await db.Contacts
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Id, x.Name, x.Pan })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            if (request.GroupByBill)
            {
                rows.AddRange(invoices.Select(x =>
                {
                    var contact = invoiceContacts[x.ContactId];
                    // FR-5.8 (Phase 23). ExportValue is the invoice's own total, reported in its own statutory column --
                    // and deliberately NOT added to TaxableValue. An export sale is zero-rated, so its lines are ZeroVat
                    // (Invoice.AddLine enforces that), and the "VatAmount == 0 => tax-exempt" split keeps it out of
                    // Taxable. Letting an export sale inflate Taxable Sales is exactly the wrong-column error phase 6's
                    // bug #3 is the reminder for.
                    return new SalesRegisterRowDto(
                        x.Date, DocumentType.Invoice, x.Code, x.ContactId, contact.Name, contact.Pan,
                        x.Buckets.Total, x.Buckets.TaxExempt, x.Buckets.Taxable, x.Buckets.Vat,
                        ExportValue: x.ExportValue,
                        ExportCountry: x.IsExport ? x.ExportCountry : null,
                        ExportDeclarationNo: x.IsExport ? x.ExportDeclarationNo : null,
                        ExportDeclarationDate: x.IsExport ? x.ExportDeclarationDate : null);
                }));
            }
            else
            {
                // Phase 36 -- one row per line instead, from the same folded lines, so a line's four magnitudes sum to
                // its document's.
                rows.AddRange(invoices.SelectMany(invoice =>
                {
                    var contact = invoiceContacts[invoice.ContactId];
                    return invoice.Lines.Select(line =>
                    {
                        var product = lineProductLookup.GetValueOrDefault(line.ProductId);
                        var split = SalesBookReader.Split([line]);
                        return new SalesRegisterRowDto(
                            invoice.Date, DocumentType.Invoice, invoice.Code, invoice.ContactId, contact.Name, contact.Pan,
                            split.Total, split.TaxExempt, split.Taxable, split.Vat,
                            ExportValue: invoice.IsExport ? split.Total : 0,
                            ExportCountry: invoice.IsExport ? invoice.ExportCountry : null,
                            ExportDeclarationNo: invoice.IsExport ? invoice.ExportDeclarationNo : null,
                            ExportDeclarationDate: invoice.IsExport ? invoice.ExportDeclarationDate : null,
                            ItemName: product.Name ?? string.Empty,
                            Quantity: line.Quantity,
                            Unit: product.Unit ?? string.Empty);
                    });
                }));
            }
        }

        // Phase 31: the Include Credit Note In Calculation toggle is the second reason this block
        // can be skipped. Both reasons remove the rows themselves, so every total below is net of
        // whichever notes actually rendered -- which is exactly what the live screen showed when the
        // toggle was cleared (19 rows to 8, taxable 71,324.41 to 139,280.06).
        if (!tagFilterActive && request.IncludeCreditNotes)
        {
            // Phase 26c: the credit-note half now comes from SalesReturnReader, which the new Sales
            // Return Register also reads -- so the two registers show the same magnitudes for the
            // same notes by construction. This register renders them negative (confirmed live: the
            // Sales Register prints them parenthesised and nets its Total of them); the return
            // register renders them positive.
            var creditNotes = await SalesReturnReader.LoadAsync(
                db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
                request.LocationId, reportLocations, request.Channel);

            // Phase 42 -- the tenant's contacts, for the reason recorded on the invoice half.
            var creditNoteContacts = await db.Contacts
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Id, x.Name, x.Pan })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            if (!request.GroupByBill)
            {
                // The same expansion on the return side, through SalesReturnReader's own line rows
                // so the Sales Return Register and this one still cannot disagree.
                rows.AddRange(creditNotes.SelectMany(note =>
                {
                    var contact = creditNoteContacts[note.ContactId];
                    return (note.Lines ?? []).Select(line => new SalesRegisterRowDto(
                        note.Date, DocumentType.CreditNote, note.Code, note.ContactId, contact.Name, contact.Pan,
                        -line.Buckets.Total, -line.Buckets.TaxExempt, -line.Buckets.Taxable, -line.Buckets.Vat,
                        ExportValue: 0, ExportCountry: null, ExportDeclarationNo: null, ExportDeclarationDate: null,
                        ItemName: line.ItemName, Quantity: line.Quantity, Unit: line.Unit));
                }));

                return Finish(request, rows);
            }

            rows.AddRange(creditNotes.Select(x =>
            {
                var contact = creditNoteContacts[x.ContactId];
                // A CreditNote carries no export block of its own -- there is no export flag on the
                // aggregate and the live reference product does not offer one -- so these stay empty
                // here rather than being derived from the Invoice it reverses.
                return new SalesRegisterRowDto(
                    x.Date, DocumentType.CreditNote, x.Code, x.ContactId, contact.Name, contact.Pan,
                    Negated(x.Buckets.Total), Negated(x.Buckets.TaxExempt), Negated(x.Buckets.Taxable),
                    Negated(x.Buckets.Vat),
                    ExportValue: 0, ExportCountry: null, ExportDeclarationNo: null, ExportDeclarationDate: null);
            }));
        }

        return Finish(request, rows);
    }

    /// <summary>Phase 90 -- a return's figure, negated without giving a zero decimal's sign bit: <c>-0m</c> is
    /// serialised and written to a cell as "-0" (phase-26c bug #1), which the export showed on every note's empty
    /// bucket.</summary>
    private static decimal Negated(decimal value) => value == 0 ? 0 : -value;

    /// <summary>Ordering, paging and the footer totals -- shared by both the grouped and the
    /// per-line path, so the footer is the same sum either way (phase 36).</summary>
    private static SalesRegisterDto Finish(SalesRegisterQuery request, List<SalesRegisterRowDto> rows)
    {
        var orderedRows = rows.OrderBy(x => x.Date).ThenBy(x => x.DocumentCode).ToList();
        var paged = request.ExportAll ? orderedRows.ToUnpagedResult() : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new SalesRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            orderedRows.Sum(x => x.TotalValue), orderedRows.Sum(x => x.TaxExemptValue),
            orderedRows.Sum(x => x.TaxableValue), orderedRows.Sum(x => x.VatAmount));
    }
}
