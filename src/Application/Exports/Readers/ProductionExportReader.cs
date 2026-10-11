using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 80 -- Production Orders (what was planned) and Production Journals (what was made), one row per part of each.
///
/// <para><b>A row per part, and a Row column naming it.</b> A production document has four kinds of part:
/// <i>Output</i> (the finished product and quantity, from the header), <i>Raw Material</i>, <i>By-Product</i> and
/// <i>Expense</i>. The output is a row rather than a header column because on a journal it carries figures of its own:
/// the finished goods' unit cost and total. Quantities are as written, positive; the Row column says whether a quantity
/// went in or came out. Stock Movements already has the journal's quantities as movements, but not which output each
/// one made, nor the expenses.</para>
///
/// <para><b>Costs only where the document has them.</b> A Production Order is a plan and carries none; a journal's
/// are written at Approve (phase 25) and are empty on a draft. Cells are left empty rather than zero, so an empty cost
/// cannot be read as a free one. A journal's three cost totals (raw materials, expenses, allocated to by-products) are
/// the sums of its rows and are not repeated. Quantities are in the product's primary unit, the only unit a
/// manufacturing line has (phase 54).</para>
///
/// <para>Eight queries merged in memory, phase 77's shape, each ordered and capped on its own.</para>
/// </summary>
public sealed class ProductionExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.Production;

    public string SheetName => "Production";

    /// <summary>Filtered on the document's Date, the date this sheet shows.</summary>
    public bool IsDateFiltered => true;

    /// <summary>Every figure a production document stores that this sheet carries; <c>ManufacturingExportTests</c>
    /// holds the eight types to it.</summary>
    public static readonly IReadOnlySet<(Type Type, string Property)> ExportedFigures = new HashSet<(Type, string)>
    {
        (typeof(ProductionOrder), nameof(ProductionOrder.OutputQuantity)),
        (typeof(ProductionOrderRawMaterialLine), nameof(ProductionOrderRawMaterialLine.Quantity)),
        (typeof(ProductionOrderByProductLine), nameof(ProductionOrderByProductLine.Quantity)),
        (typeof(ProductionOrderByProductLine), nameof(ProductionOrderByProductLine.CostAllocationPct)),
        (typeof(ProductionOrderExpenseLine), nameof(ProductionOrderExpenseLine.Amount)),
        (typeof(ProductionJournal), nameof(ProductionJournal.OutputQuantity)),
        (typeof(ProductionJournal), nameof(ProductionJournal.FinishedGoodsUnitCost)),
        (typeof(ProductionJournal), nameof(ProductionJournal.FinishedGoodsCost)),
        (typeof(ProductionJournalRawMaterialLine), nameof(ProductionJournalRawMaterialLine.Quantity)),
        (typeof(ProductionJournalRawMaterialLine), nameof(ProductionJournalRawMaterialLine.ConsumedUnitCost)),
        (typeof(ProductionJournalRawMaterialLine), nameof(ProductionJournalRawMaterialLine.Amount)),
        (typeof(ProductionJournalByProductLine), nameof(ProductionJournalByProductLine.Quantity)),
        (typeof(ProductionJournalByProductLine), nameof(ProductionJournalByProductLine.CostAllocationPct)),
        (typeof(ProductionJournalByProductLine), nameof(ProductionJournalByProductLine.AllocatedUnitCost)),
        (typeof(ProductionJournalByProductLine), nameof(ProductionJournalByProductLine.AllocatedAmount)),
        (typeof(ProductionJournalExpenseLine), nameof(ProductionJournalExpenseLine.Amount)),
    };

    public IReadOnlyList<string> Headers { get; } =
    [
        "Document Type",
        "Document No",
        "Date",
        "Status",
        "Reference",
        "Against Order",
        "Row",
        "Product Code",
        "Product Name",
        "Cost Term",
        "Quantity",
        "Unit",
        "Cost Allocation %",
        "Unit Cost",
        "Amount",
        "Warehouse",
        "Location",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var orders = db.ProductionOrders.Where(doc => doc.OrganizationId == organizationId
                                                     && (range.From == null || doc.Date >= range.From)
                                                     && (range.To == null || doc.Date <= range.To));
        var journals = db.ProductionJournals.Where(doc => doc.OrganizationId == organizationId
                                                         && (range.From == null || doc.Date >= range.From)
                                                         && (range.To == null || doc.Date <= range.To));

        const string Order = "Production Order";
        const string Journal = "Production Journal";

        var orderOutputs =
            from doc in orders
            orderby doc.Date, doc.Code
            select new ProductionRow(
                Order, doc.Code, doc.Date, doc.Status.ToString(), doc.Reference, null, PartKind.Output,
                doc.ProductId, null, doc.OutputQuantity, null, null, null, null, doc.LocationId);

        var orderRawMaterials =
            from line in db.ProductionOrderRawMaterialLines
            join doc in orders on line.ProductionOrderId equals doc.Id
            orderby doc.Date, doc.Code
            select new ProductionRow(
                Order, doc.Code, doc.Date, doc.Status.ToString(), doc.Reference, null, PartKind.RawMaterial,
                line.ProductId, null, line.Quantity, null, null, null, null, doc.LocationId);

        var orderByProducts =
            from line in db.ProductionOrderByProductLines
            join doc in orders on line.ProductionOrderId equals doc.Id
            orderby doc.Date, doc.Code
            select new ProductionRow(
                Order, doc.Code, doc.Date, doc.Status.ToString(), doc.Reference, null, PartKind.ByProduct,
                line.ProductId, null, line.Quantity, line.CostAllocationPct, null, null, null, doc.LocationId);

        var orderExpenses =
            from line in db.ProductionOrderExpenseLines
            join doc in orders on line.ProductionOrderId equals doc.Id
            orderby doc.Date, doc.Code
            select new ProductionRow(
                Order, doc.Code, doc.Date, doc.Status.ToString(), doc.Reference, null, PartKind.Expense,
                null, line.CostTermId, null, null, null, line.Amount, null, doc.LocationId);

        // A journal names the order it was raised from by phase 6's referrer.
        var journalHeaders =
            from doc in journals
            join order in db.ProductionOrders
                on (doc.ReferrerType == DocumentType.ProductionOrder ? doc.ReferrerId : null) equals (Guid?)order.Id into referred
            from order in referred.DefaultIfEmpty()
            select new { doc, AgainstOrder = order == null ? null : order.Code };

        var journalOutputs =
            from x in journalHeaders
            orderby x.doc.Date, x.doc.Code
            select new ProductionRow(
                Journal, x.doc.Code, x.doc.Date, x.doc.Status.ToString(), x.doc.Reference, x.AgainstOrder, PartKind.Output,
                x.doc.ProductId, null, x.doc.OutputQuantity, null, x.doc.FinishedGoodsUnitCost, x.doc.FinishedGoodsCost,
                x.doc.WarehouseId, x.doc.LocationId);

        var journalRawMaterials =
            from line in db.ProductionJournalRawMaterialLines
            join x in journalHeaders on line.ProductionJournalId equals x.doc.Id
            orderby x.doc.Date, x.doc.Code
            select new ProductionRow(
                Journal, x.doc.Code, x.doc.Date, x.doc.Status.ToString(), x.doc.Reference, x.AgainstOrder, PartKind.RawMaterial,
                line.ProductId, null, line.Quantity, null, line.ConsumedUnitCost, line.Amount,
                x.doc.WarehouseId, x.doc.LocationId);

        var journalByProducts =
            from line in db.ProductionJournalByProductLines
            join x in journalHeaders on line.ProductionJournalId equals x.doc.Id
            orderby x.doc.Date, x.doc.Code
            select new ProductionRow(
                Journal, x.doc.Code, x.doc.Date, x.doc.Status.ToString(), x.doc.Reference, x.AgainstOrder, PartKind.ByProduct,
                line.ProductId, null, line.Quantity, line.CostAllocationPct, line.AllocatedUnitCost, line.AllocatedAmount,
                x.doc.WarehouseId, x.doc.LocationId);

        var journalExpenses =
            from line in db.ProductionJournalExpenseLines
            join x in journalHeaders on line.ProductionJournalId equals x.doc.Id
            orderby x.doc.Date, x.doc.Code
            select new ProductionRow(
                Journal, x.doc.Code, x.doc.Date, x.doc.Status.ToString(), x.doc.Reference, x.AgainstOrder, PartKind.Expense,
                null, line.CostTermId, null, null, null, line.Amount, x.doc.WarehouseId, x.doc.LocationId);

        var totalRowCount = 0;
        var merged = new List<ProductionRow>();

        foreach (var query in new[]
                 {
                     orderOutputs, orderRawMaterials, orderByProducts, orderExpenses,
                     journalOutputs, journalRawMaterials, journalByProducts, journalExpenses,
                 })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var names = await ManufacturingNames.LoadAsync(db, organizationId, cancellationToken);

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

    private sealed record ProductionRow(
        string DocumentType,
        string DocumentCode,
        DateOnly Date,
        string Status,
        string? Reference,
        string? AgainstOrder,
        PartKind Kind,
        Guid? ProductId,
        Guid? CostTermId,
        decimal? Quantity,
        decimal? CostAllocationPct,
        decimal? UnitCost,
        decimal? Amount,
        Guid? WarehouseId,
        Guid? LocationId)
    {
        public object?[] ToCells(ManufacturingNames names)
        {
            var product = names.Product(ProductId);
            return
            [
                DocumentType,
                DocumentCode,
                ExportCell.LocalDate(Date),
                Status,
                Reference,
                AgainstOrder,
                Kind.Label(),
                product?.Code,
                product?.Name,
                names.CostTerms.NameOf(CostTermId),
                Quantity,
                product is null ? null : names.Units.NameOf(product.PrimaryUnitId),
                CostAllocationPct,
                UnitCost,
                Amount,
                names.Warehouses.NameOf(WarehouseId),
                names.Locations.NameOf(LocationId),
            ];
        }
    }
}

/// <summary>Phase 80 -- the four kinds of part a Bill of Materials or a production document has, in the order a
/// document's rows print.</summary>
internal enum PartKind
{
    Output,
    RawMaterial,
    ByProduct,
    Expense,
}

internal static class PartKindLabels
{
    public static string Label(this PartKind kind) => kind switch
    {
        PartKind.Output => "Output",
        PartKind.RawMaterial => "Raw Material",
        PartKind.ByProduct => "By-Product",
        PartKind.Expense => "Expense",
        _ => kind.ToString(),
    };
}

/// <summary>
/// Phase 80 -- the names the two manufacturing sheets print. Products are looked up from the tenant's whole catalogue
/// (code, name and primary unit) for <see cref="ExportLookups"/>' reason: it is bounded by the tenant, not the period,
/// and a manufacturing row names a product in three different roles, so a join per role would triple each query.
/// </summary>
internal sealed record ManufacturingNames(
    IReadOnlyDictionary<Guid, ManufacturingNames.ProductName> Products,
    IReadOnlyDictionary<Guid, string> Units,
    IReadOnlyDictionary<Guid, string> CostTerms,
    IReadOnlyDictionary<Guid, string> Warehouses,
    IReadOnlyDictionary<Guid, string> Locations)
{
    public sealed record ProductName(string Code, string Name, Guid PrimaryUnitId);

    public ProductName? Product(Guid? id) => id is { } key ? Products.GetValueOrDefault(key) : null;

    public static async Task<ManufacturingNames> LoadAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        new(
            await db.Products
                .Where(x => x.OrganizationId == organizationId)
                .Select(x => new { x.Id, x.Code, x.Name, x.PrimaryUnitId })
                .ToDictionaryAsync(x => x.Id, x => new ProductName(x.Code, x.Name, x.PrimaryUnitId), ct),
            await ExportLookups.UnitsAsync(db, organizationId, ct),
            await ExportLookups.CostTermsAsync(db, organizationId, ct),
            await ExportLookups.WarehousesAsync(db, organizationId, ct),
            await ExportLookups.LocationsAsync(db, organizationId, ct));
}
