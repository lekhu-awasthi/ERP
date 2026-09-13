using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// The purchase mirror of <see cref="SalesDocumentExportReader"/>: one row per Purchase Bill or
/// Debit Note <i>line</i>. Same fourteen columns through the same <see cref="DocumentLineRow"/>, and
/// the same rules -- returns negated so the Amount column sums to net purchases, drafts included and
/// labelled by Status.
///
/// <para>The two readers are deliberately mirrors rather than one generic reader over a document
/// type: phase 26b's Sales/Purchase pairs established that shape, and the moment one side needs a
/// column the other does not -- this side already has TDS and an import declaration on the header --
/// a shared generic would have to grow a discriminator anyway.</para>
/// </summary>
public sealed class PurchaseDocumentExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.PurchaseDocuments;

    public string SheetName => "Purchase Documents";

    public bool IsDateFiltered => true;

    public IReadOnlyList<string> Headers { get; } =
    [
        "Document Type",
        "Document No",
        "Date",
        "Status",
        "Supplier",
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
        var bills =
            from line in db.PurchaseBillLines
            join doc in db.PurchaseBills on line.PurchaseBillId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            select new
            {
                DocumentType = "Purchase Bill",
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

        var debitNotes =
            from line in db.DebitNoteLines
            join doc in db.DebitNotes on line.DebitNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            select new
            {
                DocumentType = "Debit Note",
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
        var query = bills
            .Concat(debitNotes)
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
