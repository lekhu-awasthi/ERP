using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// The purchase mirror of <see cref="SalesDocumentExportReader"/>: one row per Purchase Bill, Debit Note or Expense
/// <i>line</i>. Phase 38's fourteen columns through the same <see cref="DocumentLineRow"/>, and the same rules --
/// returns negated so the Amount column sums to net purchases, drafts included and labelled by Status.
///
/// <para>The two readers are deliberately mirrors rather than one generic reader over a document
/// type: phase 26b's Sales/Purchase pairs established that shape, and the moment one side needs a
/// column the other does not -- this side already has TDS and an import declaration on the header --
/// a shared generic would have to grow a discriminator anyway.</para>
///
/// <para><b>Phase 78 -- the rest of a purchase's money, appended after phase 38's fourteen</b> as phase 77 did for sales,
/// so a reader that located a column by position still finds it:</para>
/// <list type="bullet">
/// <item><b>Expenses</b> are on the sheet. An expense is a purchase with account lines instead of product lines (Annex
/// 13 and the TDS report already read it so), so its rows leave Product Code and Name empty, carry zero Quantity and
/// Rate as phase 77's round-off row does, and name their account in <b>Account</b>.</item>
/// <item><b>TDS is a row of its own</b> after a document's lines, for phase 77's round-off reason: it is a figure of
/// the document, true of no line, and repeated on each line its column would sum wrong. It is what was withheld, so
/// what the supplier is owed is Amount + VAT Amount - TDS. A debit note's is negated with its figures.</item>
/// <item><b>An additional cost (phase 29) is a row of its own</b> too, naming its cost term and, when it names one,
/// its product. It is <i>not</i> part of the bill's total: the supplier is credited only the goods, and the cost is
/// owed to whoever carried them (confirmed live 2026-09-04). So <b>Additional Cost</b> is its own column, never summed
/// into Amount, and in the bill's currency like everything else here. What it added to the stock is base currency and
/// on the Stock Movements sheet.</item>
/// <item>A debit note names the bill it is against (<b>Against Bill</b>), and its line's <b>Expenditure
/// Classification</b> and <b>Import</b> are its bill's, matched exactly as the statutory registers match them
/// (<see cref="Purchasing.Reports.PurchaseReturnReader"/>: bill, product, rate and VAT rate, falling back to Others and
/// local), so the sheet and Annex 13 cannot disagree about one return.</item>
/// <item><b>Unit</b>, <b>Exchange Rate</b> and <b>Location</b> follow, as on the sales sheet.</item>
/// <item>Phase 80 -- <b>Allocation Method</b> closes the sheet: how an additional cost row was spread over the bill's
/// lines (<i>Value</i> or <i>Quantity</i>), empty on every other kind of row. The allocation it produced is derived at
/// Approve; the method is what a reader needs to re-derive it.</item>
/// </list>
///
/// <para><b>Seven queries merged in memory, not one <c>Concat</c></b> (phase 77's reason: the kinds of row differ in
/// which columns they have). Each is ordered and capped on its own; the total is the sum of the counts.</para>
/// </summary>
public sealed class PurchaseDocumentExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.PurchaseDocuments;

    public string SheetName => "Purchase Documents";

    public bool IsDateFiltered => true;

    /// <summary>
    /// Phase 78 -- every money figure a purchase document stores that this sheet carries, by type and property. The
    /// guard in <c>PurchaseExportTests</c> holds every <c>decimal</c> on the six types to this list or a named reason,
    /// so the next part of a purchase's money cannot be added without deciding whether it is exported.
    /// </summary>
    public static readonly IReadOnlySet<(Type Type, string Property)> ExportedMoney = new HashSet<(Type, string)>
    {
        (typeof(PurchaseBillLine), nameof(PurchaseBillLine.Quantity)),
        (typeof(PurchaseBillLine), nameof(PurchaseBillLine.Rate)),
        (typeof(PurchaseBillLine), nameof(PurchaseBillLine.DiscountPct)),
        (typeof(PurchaseBillLine), nameof(PurchaseBillLine.Amount)),
        (typeof(PurchaseBillLine), nameof(PurchaseBillLine.VatAmount)),
        (typeof(DebitNoteLine), nameof(DebitNoteLine.Quantity)),
        (typeof(DebitNoteLine), nameof(DebitNoteLine.Rate)),
        (typeof(DebitNoteLine), nameof(DebitNoteLine.DiscountPct)),
        (typeof(DebitNoteLine), nameof(DebitNoteLine.Amount)),
        (typeof(DebitNoteLine), nameof(DebitNoteLine.VatAmount)),
        (typeof(ExpenseLine), nameof(ExpenseLine.Amount)),
        (typeof(ExpenseLine), nameof(ExpenseLine.VatAmount)),
        (typeof(PurchaseBill), nameof(PurchaseBill.DiscountPct)),
        (typeof(PurchaseBill), nameof(PurchaseBill.TdsAmount)),
        (typeof(PurchaseBill), nameof(PurchaseBill.ExchangeRate)),
        (typeof(DebitNote), nameof(DebitNote.DiscountPct)),
        (typeof(DebitNote), nameof(DebitNote.TdsAmount)),
        (typeof(DebitNote), nameof(DebitNote.ExchangeRate)),
        (typeof(Expense), nameof(Expense.TdsAmount)),
        (typeof(Expense), nameof(Expense.ExchangeRate)),
        (typeof(PurchaseBillAdditionalCost), nameof(PurchaseBillAdditionalCost.Amount)),
    };

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
        "Against Bill",
        "Supplier Invoice Ref",
        "Account",
        "Expenditure Classification",
        "Import",
        "Document Discount %",
        "TDS",
        "Cost Term",
        "Additional Cost",
        "Unit",
        "Exchange Rate",
        "Location",
        "Allocation Method",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var billLines =
            from line in db.PurchaseBillLines
            join doc in db.PurchaseBills on line.PurchaseBillId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Purchase Bill", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                null, doc.SupplierInvoiceReference, null, line.ExpenditureClassification, doc.IsImport,
                doc.DiscountPct, 0m, null, 0m,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId,
                null, RowKind.Line);

        // A Debit Note's figures are negated so the columns sum to net purchases; its rates and percentages are not.
        // Its bill is phase 6's referrer; a standalone note has none, and classifies as Others and local, as the
        // registers have it.
        var debitNoteLines =
            from line in db.DebitNoteLines
            join doc in db.DebitNotes on line.DebitNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join bill in db.PurchaseBills
                on (doc.ReferrerType == DocumentType.PurchaseBill ? doc.ReferrerId : null) equals (Guid?)bill.Id into bills
            from bill in bills.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Debit Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                -line.Quantity, line.Rate, line.DiscountPct, -line.Amount, -line.VatAmount,
                bill == null ? null : bill.Code, null, null,
                (bill == null
                    ? null
                    : (from source in db.PurchaseBillLines
                       where source.PurchaseBillId == bill.Id && source.ProductId == line.ProductId
                             && source.Rate == line.Rate && source.VatRate == line.VatRate
                       select (ExpenditureClassification?)source.ExpenditureClassification).FirstOrDefault())
                    ?? ExpenditureClassification.Others,
                bill != null && bill.IsImport,
                doc.DiscountPct, 0m, null, 0m,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId,
                null, RowKind.Line);

        var expenseLines =
            from line in db.ExpenseLines
            join doc in db.Expenses on line.ExpenseId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Expense", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, null, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, line.Amount, line.VatAmount,
                null, doc.SupplierInvoiceReference, line.AccountId, null, null,
                0m, 0m, null, 0m,
                null, doc.ExchangeRate, doc.LocationId,
                null, RowKind.Line);

        var additionalCosts =
            from cost in db.PurchaseBillAdditionalCosts
            join doc in db.PurchaseBills on cost.PurchaseBillId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on cost.ProductId equals (Guid?)product.Id into products
            from product in products.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Purchase Bill", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                0m, 0m, 0m, 0m, 0m,
                null, doc.SupplierInvoiceReference, null, null, doc.IsImport,
                doc.DiscountPct, 0m, cost.CostTermId, cost.Amount,
                null, doc.ExchangeRate, doc.LocationId,
                (AdditionalCostMethod?)cost.Method, RowKind.AdditionalCost);

        var billTds =
            from doc in db.PurchaseBills
            where doc.OrganizationId == organizationId
                  && doc.TdsAmount != 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Purchase Bill", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, 0m, 0m,
                null, doc.SupplierInvoiceReference, null, null, doc.IsImport,
                doc.DiscountPct, doc.TdsAmount, null, 0m,
                null, doc.ExchangeRate, doc.LocationId,
                null, RowKind.Tds);

        var debitNoteTds =
            from doc in db.DebitNotes
            where doc.OrganizationId == organizationId
                  && doc.TdsAmount != 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join bill in db.PurchaseBills
                on (doc.ReferrerType == DocumentType.PurchaseBill ? doc.ReferrerId : null) equals (Guid?)bill.Id into bills
            from bill in bills.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Debit Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, 0m, 0m,
                bill == null ? null : bill.Code, null, null, null, bill != null && bill.IsImport,
                doc.DiscountPct, -doc.TdsAmount, null, 0m,
                null, doc.ExchangeRate, doc.LocationId,
                null, RowKind.Tds);

        var expenseTds =
            from doc in db.Expenses
            where doc.OrganizationId == organizationId
                  && doc.TdsAmount != 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new PurchaseRow(
                "Expense", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, null, doc.CurrencyCode,
                null, null, 0m, 0m, 0m, 0m, 0m,
                null, doc.SupplierInvoiceReference, null, null, null,
                0m, doc.TdsAmount, null, 0m,
                null, doc.ExchangeRate, doc.LocationId,
                null, RowKind.Tds);

        var totalRowCount = 0;
        var merged = new List<PurchaseRow>();

        foreach (var query in new[] { billLines, debitNoteLines, expenseLines, additionalCosts, billTds, debitNoteTds, expenseTds })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var names = new Names(
            await ExportLookups.UnitsAsync(db, organizationId, cancellationToken),
            await ExportLookups.LocationsAsync(db, organizationId, cancellationToken),
            await ExportLookups.AccountsAsync(db, organizationId, cancellationToken),
            await ExportLookups.CostTermsAsync(db, organizationId, cancellationToken));

        var rows = merged
            .OrderBy(r => r.Date)
            .ThenBy(r => r.DocumentType, StringComparer.Ordinal)
            .ThenBy(r => r.DocumentCode, StringComparer.Ordinal)
            .ThenBy(r => r.Kind)
            .Take(maxRows)
            .Select(r => r.ToCells(names))
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    /// <summary>The order a document's rows print in: its lines, then what it adds outside them, then what it
    /// withholds.</summary>
    private enum RowKind
    {
        Line,
        AdditionalCost,
        Tds,
    }

    private sealed record Names(
        IReadOnlyDictionary<Guid, string> Units,
        IReadOnlyDictionary<Guid, string> Locations,
        IReadOnlyDictionary<Guid, string> Accounts,
        IReadOnlyDictionary<Guid, string> CostTerms);

    /// <summary>One row of the sheet, as every query projects it. Every member is a plain value or an id; names are
    /// resolved after the page is read (<see cref="ExportLookups"/>).</summary>
    private sealed record PurchaseRow(
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
        string? AgainstBill,
        string? SupplierInvoiceReference,
        Guid? AccountId,
        ExpenditureClassification? Classification,
        bool? IsImport,
        decimal DocumentDiscountPct,
        decimal Tds,
        Guid? CostTermId,
        decimal AdditionalCost,
        Guid? UnitId,
        decimal ExchangeRate,
        Guid? LocationId,
        AdditionalCostMethod? Method,
        RowKind Kind)
    {
        public object?[] ToCells(Names names) =>
        [
            .. new DocumentLineRow(
                DocumentType, DocumentCode, Date, Status, ContactName, Reference, CurrencyCode, ProductCode,
                ProductName, Quantity, Rate, DiscountPct, Amount, VatAmount).ToCells(),
            AgainstBill,
            SupplierInvoiceReference,
            names.Accounts.NameOf(AccountId),
            Classification?.ToString(),
            IsImport switch { true => "Yes", false => "No", null => null },
            DocumentDiscountPct,
            Tds,
            names.CostTerms.NameOf(CostTermId),
            AdditionalCost,
            names.Units.NameOf(UnitId),
            ExchangeRate,
            names.Locations.NameOf(LocationId),
            Method?.ToString(),
        ];
    }
}
