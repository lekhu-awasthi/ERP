using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
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
/// </summary>
public sealed class SalesDocumentExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.SalesDocuments;

    public string SheetName => "Sales Documents";

    public bool IsDateFiltered => true;

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
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var invoices =
            from line in db.InvoiceLines
            join doc in db.Invoices on line.InvoiceId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            select new
            {
                DocumentType = "Invoice",
                DocumentCode = doc.Code,
                doc.Date,
                Status = doc.Status.ToString(),
                ContactName = contact == null ? null : contact.Name,
                doc.Reference,
                doc.CurrencyCode,
                ProductCode = product == null ? null : product.Code,
                ProductName = product == null ? null : product.Name,
                line.Quantity,
                line.Rate,
                line.DiscountPct,
                line.Amount,
                line.VatAmount,
            };

        var creditNotes =
            from line in db.CreditNoteLines
            join doc in db.CreditNotes on line.CreditNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            select new
            {
                DocumentType = "Credit Note",
                DocumentCode = doc.Code,
                doc.Date,
                Status = doc.Status.ToString(),
                ContactName = contact == null ? null : contact.Name,
                doc.Reference,
                doc.CurrencyCode,
                ProductCode = product == null ? null : product.Code,
                ProductName = product == null ? null : product.Name,
                Quantity = -line.Quantity,
                line.Rate,
                line.DiscountPct,
                Amount = -line.Amount,
                VatAmount = -line.VatAmount,
            };

        // The two halves are Concat-ed while they are still anonymous, and only the materialised
        // page is turned into DocumentLineRow. EF refuses a set operation "after client projection
        // has been applied": a constructor call is a client projection, so Concat-ing two
        // `select new DocumentLineRow(...)` queries throws at run time -- and not on InMemory alone,
        // which is what makes it worth the comment. The shared record stays; what moved is where it
        // is built.
        var query = invoices
            .Concat(creditNotes)
            .OrderBy(r => r.Date)
            .ThenBy(r => r.DocumentType)
            .ThenBy(r => r.DocumentCode);

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        var rows = page
            .Select(r => new DocumentLineRow(
                r.DocumentType,
                r.DocumentCode,
                r.Date,
                r.Status,
                r.ContactName,
                r.Reference,
                r.CurrencyCode,
                r.ProductCode,
                r.ProductName,
                r.Quantity,
                r.Rate,
                r.DiscountPct,
                r.Amount,
                r.VatAmount).ToCells())
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
