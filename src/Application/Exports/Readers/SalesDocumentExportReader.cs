using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 38's sales half of "what the General Ledger cannot say": one row per Invoice or Credit Note
/// <i>line</i>, with its document's header repeated alongside it.
///
/// <para><b>Lines, not headers, and that is the whole reason the category exists.</b> A header sheet
/// would duplicate what Ledger Transactions already carries in account form. What the ledger has no
/// column for is the trade itself -- which product, how many, at what unit rate, at what discount,
/// at what VAT -- and that only exists on the line. A reader taking this data out to reconcile
/// against a supplier, a tax return or a successor system needs the line.</para>
///
/// <para><b>Two document types on one sheet, distinguished by a Document Type column and a signed
/// quantity.</b> A Credit Note is a sales return, so its quantities and amounts are negated here;
/// summing this sheet's Amount column therefore gives net sales, which is what somebody adding it up
/// means. Keeping them on separate sheets would have made that sum a manual subtraction and made
/// the eight-category budget harder to reason about for no gain.</para>
///
/// <para><b>Drafts are included, and labelled.</b> This is an export of the tenant's data, not a
/// report: a draft invoice is data the tenant entered and would expect to find. The Status column is
/// what separates it from an approved one, and Voided documents carry that status too rather than
/// vanishing.</para>
///
/// <para><b>Phase 77 -- every part of a line's money, and the document's round-off.</b> Phases 61-74 gave a line a
/// service charge, a packaging fee, a per-unit discount and a share of its bill's Amount discount, and a document a
/// round-off; this sheet carried none of them, so its columns stopped adding up to what a customer was charged. They
/// are <b>appended</b> after phase 38's fourteen, so a reader that located a column by position still finds it. A
/// document's round-off is a figure of the document, not of any line, so it has a <b>row of its own</b> after its lines
/// (no product, every line figure zero): putting it on a line would make that line say something untrue about itself,
/// and repeating it on every line would make its column sum wrong. Summing Amount + Service Charge + Packaging Fee +
/// VAT Amount + Round Off gives what was billed, net of returns. A restaurant bill names its order (Order No), and a
/// credit note the invoice it is against (phase 69's picked or typed one, else the invoice it was raised from: a
/// till refund names its sale that way); an order's own terms reach the sheet through its bills' lines (each
/// line's Bill Discount share, a zero service charge when waived), and an unbilled tab is not exported (Decision D).
/// <see cref="ExportedLineMoney"/> names every money column of a line, and a guard test holds the Domain to it.</para>
///
/// <para><b>Phase 78 -- Unit, Exchange Rate and Location, appended the same way.</b> Since phase 52 a line's Quantity and
/// Rate are in the unit it was entered in, so "2" was 2 of something the sheet never said; the line's own unit is
/// printed, or the product's primary unit when the line names none (which is what a line with no unit means). A
/// foreign-currency document's figures are in its currency, and the rate is what turns them into rupees. The location
/// is the billing location the document was raised at (phase 35a). A round-off row carries its document's rate and
/// location and no unit.</para>
///
/// <para><b>Four queries merged in memory, not one <c>Concat</c>.</b> The round-off rows have no product and a credit
/// note has no order of its own, so a single set operation would need typed null constants on one side, which is a
/// translation this codebase has never proven on SQL Server. Each half is ordered and capped on its own and the pages are
/// merged by the sheet's ordering; the total is the sum of the four counts.</para>
/// </summary>
public sealed class SalesDocumentExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.SalesDocuments;

    public string SheetName => "Sales Documents";

    public bool IsDateFiltered => true;

    /// <summary>
    /// Phase 77 -- the money of an <see cref="InvoiceLine"/> and a <see cref="CreditNoteLine"/> this sheet carries, by
    /// property name. <c>TillExportTests</c> asserts every <c>decimal</c> property of both line types is here
    /// or named there as not money (a rate, a factor, a cost), so the next part of a line's money cannot be added
    /// without deciding whether it is exported. Phase 78 holds the two documents' own decimals the same way
    /// (<see cref="ExportedDocumentMoney"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> ExportedLineMoney = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(InvoiceLine.Quantity),
        nameof(InvoiceLine.Rate),
        nameof(InvoiceLine.DiscountPct),
        nameof(InvoiceLine.DiscountPerUnit),
        nameof(InvoiceLine.BillDiscountAmount),
        nameof(InvoiceLine.Amount),
        nameof(InvoiceLine.ServiceChargeAmount),
        nameof(InvoiceLine.PackagingFeeAmount),
        nameof(InvoiceLine.VatAmount),
    };

    /// <summary>Phase 78 -- the figures an <see cref="Invoice"/> and a <see cref="CreditNote"/> store of their own that
    /// this sheet carries: Document Discount %, Round Off and Exchange Rate.</summary>
    public static readonly IReadOnlySet<string> ExportedDocumentMoney = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Invoice.DiscountPct),
        nameof(Invoice.RoundOff),
        nameof(Invoice.ExchangeRate),
    };

    public IReadOnlyList<string> Headers { get; } =
    [
        "Document Type",
        "Document No",
        "Date",
        "Status",
        "Customer",
        "Reference",
        "Currency",
        "Product Code",
        "Product Name",
        "Quantity",
        "Rate",
        "Discount %",
        "Amount",
        "VAT Amount",
        "Channel",
        "Order No",
        "Against Invoice",
        "Discount per Unit",
        "Document Discount %",
        "Bill Discount",
        "Service Charge",
        "Packaging Fee",
        "Round Off",
        "Unit",
        "Exchange Rate",
        "Location",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var invoiceLines =
            from line in db.InvoiceLines
            join doc in db.Invoices on line.InvoiceId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join order in db.PosOrders on doc.PosOrderId equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new SalesRow(
                "Invoice", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                doc.Channel, order == null ? null : order.Code, null,
                line.DiscountPerUnit, doc.DiscountPct, line.BillDiscountAmount,
                line.ServiceChargeAmount, line.PackagingFeeAmount, 0m, false,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        // A Credit Note's figures are negated so the columns sum to net sales; its rates and percentages are not, as
        // phase 38's Rate never was. Its order is the one its invoice billed.
        var creditNoteLines =
            from line in db.CreditNoteLines
            join doc in db.CreditNotes on line.CreditNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join invoice in db.Invoices
                on doc.AgainstInvoiceId ?? (doc.ReferrerType == DocumentType.Invoice ? doc.ReferrerId : null)
                equals (Guid?)invoice.Id into invoices
            from invoice in invoices.DefaultIfEmpty()
            join order in db.PosOrders on (invoice == null ? null : invoice.PosOrderId) equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new SalesRow(
                "Credit Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                -line.Quantity, line.Rate, line.DiscountPct, -line.Amount, -line.VatAmount,
                doc.Channel, order == null ? null : order.Code, doc.AgainstInvoiceNumber ?? (invoice == null ? null : invoice.Code),
                line.DiscountPerUnit, doc.DiscountPct, -line.BillDiscountAmount,
                -line.ServiceChargeAmount, -line.PackagingFeeAmount, 0m, false,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var invoiceRoundOffs =
            from doc in db.Invoices
            where doc.OrganizationId == organizationId
                  && doc.RoundOff != 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join order in db.PosOrders on doc.PosOrderId equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new SalesRow(
                "Invoice", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, 0m, 0m,
                doc.Channel, order == null ? null : order.Code, null,
                0m, doc.DiscountPct, 0m, 0m, 0m, doc.RoundOff, true, null, doc.ExchangeRate, doc.LocationId);

        var creditNoteRoundOffs =
            from doc in db.CreditNotes
            where doc.OrganizationId == organizationId
                  && doc.RoundOff != 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join invoice in db.Invoices
                on doc.AgainstInvoiceId ?? (doc.ReferrerType == DocumentType.Invoice ? doc.ReferrerId : null)
                equals (Guid?)invoice.Id into invoices
            from invoice in invoices.DefaultIfEmpty()
            join order in db.PosOrders on (invoice == null ? null : invoice.PosOrderId) equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new SalesRow(
                "Credit Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, 0m, 0m,
                doc.Channel, order == null ? null : order.Code, doc.AgainstInvoiceNumber ?? (invoice == null ? null : invoice.Code),
                0m, doc.DiscountPct, 0m, 0m, 0m, -doc.RoundOff, true, null, doc.ExchangeRate, doc.LocationId);

        var totalRowCount = 0;
        var merged = new List<SalesRow>();

        foreach (var query in new[] { invoiceLines, creditNoteLines, invoiceRoundOffs, creditNoteRoundOffs })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var units = await ExportLookups.UnitsAsync(db, organizationId, cancellationToken);
        var locations = await ExportLookups.LocationsAsync(db, organizationId, cancellationToken);

        var rows = merged
            .OrderBy(r => r.Date)
            .ThenBy(r => r.DocumentType, StringComparer.Ordinal)
            .ThenBy(r => r.DocumentCode, StringComparer.Ordinal)
            .ThenBy(r => r.IsRoundOff)
            .Take(maxRows)
            .Select(r => r.ToCells(units, locations))
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    /// <summary>One row of the sheet, as both halves and both kinds of row project it. Every member is a plain value
    /// (EF builds it from columns; the Concat that would refuse a constructor is not used here).</summary>
    private sealed record SalesRow(
        string DocumentType,
        string DocumentCode,
        DateOnly Date,
        string Status,
        string? ContactName,
        string? Reference,
        string CurrencyCode,
        string? ProductCode,
        string? ProductName,
        decimal Quantity,
        decimal Rate,
        decimal DiscountPct,
        decimal Amount,
        decimal VatAmount,
        SalesChannel Channel,
        string? OrderCode,
        string? AgainstInvoice,
        decimal DiscountPerUnit,
        decimal DocumentDiscountPct,
        decimal BillDiscount,
        decimal ServiceCharge,
        decimal PackagingFee,
        decimal RoundOff,
        bool IsRoundOff,
        Guid? UnitId,
        decimal ExchangeRate,
        Guid? LocationId)
    {
        public object?[] ToCells(IReadOnlyDictionary<Guid, string> units, IReadOnlyDictionary<Guid, string> locations) =>
        [
            .. new DocumentLineRow(
                DocumentType, DocumentCode, Date, Status, ContactName, Reference, CurrencyCode, ProductCode,
                ProductName, Quantity, Rate, DiscountPct, Amount, VatAmount).ToCells(),
            Channel == SalesChannel.Pos ? "POS" : "ERP",
            OrderCode,
            AgainstInvoice,
            DiscountPerUnit,
            DocumentDiscountPct,
            BillDiscount,
            ServiceCharge,
            PackagingFee,
            RoundOff,
            units.NameOf(UnitId),
            ExchangeRate,
            locations.NameOf(LocationId),
        ];
    }
}
