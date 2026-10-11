using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// Phase 80 -- a Bill of Materials and a production document, each a row per part (Output, Raw Material, By-Product,
/// Expense), with a journal's costs where it has them and empty cells where a plan has none.
/// </summary>
public sealed class ManufacturingExportTests
{
    private static class BomCol
    {
        public const int Finished = 0, Active = 2, EverySale = 3, Row = 4, Product = 5, CostTerm = 7, Quantity = 8,
            Unit = 9, Allocation = 10, Amount = 11, Notes = 12;
    }

    private static class ProdCol
    {
        public const int Type = 0, Code = 1, Status = 3, AgainstOrder = 5, Row = 6, Product = 7, CostTerm = 9, Quantity = 10,
            Unit = 11, Allocation = 12, UnitCost = 13, Amount = 14, Warehouse = 15, Location = 16;
    }

    private sealed record Seeded(Guid OrganizationId, string FinishedCode, IAppDbContext Db);

    /// <summary>
    /// Juice from fruit: 10 litres of juice and 2 kg of pulp from 20 kg of fruit, with 100 of labour. In the journal
    /// the fruit cost 20 a kg (400), so production cost 500; the pulp takes 10% (50, 25 a kg) and the juice the rest
    /// (450, 45 a litre). Every division is exact, so the conservation below has no residue to name.
    /// </summary>
    private static async Task<Seeded> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);
        var org = tenant.OrganizationId;
        var category = await db.ProductCategories.FirstAsync(x => x.OrganizationId == org);
        var warehouse = await db.Warehouses.SingleAsync(x => x.OrganizationId == org);

        var litre = UnitOfMeasurement.Create(org, "Litre", "l");
        var kilo = UnitOfMeasurement.Create(org, "Kilogram", "kg");
        db.UnitsOfMeasurement.AddRange(litre, kilo);
        var juice = Product.Create(org, ProductType.Goods, "Mango Juice", "FG-JUICE", category.Id, litre.Id, null, true, 0m, 0m, VatRate.NoVat, 0, true);
        var fruit = Product.Create(org, ProductType.Goods, "Mango", "RM-MANGO", category.Id, kilo.Id, null, true, 0m, 0m, VatRate.NoVat, 0, true);
        var pulp = Product.Create(org, ProductType.Goods, "Mango Pulp", "BP-PULP", category.Id, kilo.Id, null, true, 0m, 0m, VatRate.NoVat, 0, true);
        db.Products.AddRange(juice, fruit, pulp);
        var labour = CostTerm.Create(org, "Labour", CostTermCategory.ProductionCost);
        db.CostTerms.Add(labour);
        var location = BillingLocation.Create(org, "FAC", "Factory", null, null);
        db.BillingLocations.Add(location);

        var bom = BillOfMaterials.Create(org, juice.Id, 10m, true, "Ripe fruit only");
        bom.AddRawMaterial(fruit.Id, 20m);
        bom.AddByProduct(pulp.Id, 10m, 2m);
        bom.AddExpense(labour.Id, 100m);
        db.BillsOfMaterials.Add(bom);

        var order = ProductionOrder.Create(org, new DateOnly(2026, 9, 1), "PLAN-1", juice.Id, 10m, bom.Id, null);
        order.AddRawMaterial(fruit.Id, 20m);
        order.AddByProduct(pulp.Id, 10m, 2m);
        order.AddExpense(labour.Id, 100m);
        order.SetLocation(location.Id);
        order.Approve(tenant.AdminUserId, "PRO-0001");
        db.ProductionOrders.Add(order);

        var journal = ProductionJournal.Create(
            org, new DateOnly(2026, 9, 2), "RUN-1", juice.Id, 10m, warehouse.Id, bom.Id, null,
            DocumentType.ProductionOrder, order.Id);
        journal.AddRawMaterial(fruit.Id, 20m);
        journal.AddByProduct(pulp.Id, 10m, 2m);
        journal.AddExpense(labour.Id, 100m);
        journal.SetLocation(location.Id);
        journal.RawMaterials[0].RecordConsumedCost(20m, 400m);
        journal.ComputeAndRecordRollUp();
        journal.Approve(tenant.AdminUserId, "PJ-0001");
        db.ProductionJournals.Add(journal);

        await db.SaveChangesAsync();
        return new Seeded(org, juice.Code, db);
    }

    private static async Task<IReadOnlyList<object?[]>> ReadAsync(IExportCategoryReader reader, Seeded s, ExportDateRange? range = null) =>
        (await reader.ReadAsync(s.OrganizationId, 1000, range ?? ExportDateRange.Unbounded, CancellationToken.None)).Rows;

    private static decimal D(object? cell) => (decimal)cell!;

    [Fact]
    public async Task A_bill_of_materials_prints_its_output_then_each_part_and_ignores_the_period()
    {
        var s = await SeedAsync();
        var reader = new BillOfMaterialsExportReader(s.Db);
        Assert.False(reader.IsDateFiltered);

        // A date range far from anything: master data is not narrowed by it.
        var rows = (await ReadAsync(reader, s, new ExportDateRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2))))
            .Where(r => Equals(r[BomCol.Finished], s.FinishedCode))
            .ToList();
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));

        Assert.Equal(["Output", "Raw Material", "By-Product", "Expense"], rows.Select(r => (string)r[BomCol.Row]!));
        Assert.All(rows, r => Assert.Equal(("Yes", "Yes", "Ripe fruit only"),
            ((string)r[BomCol.Active]!, (string)r[BomCol.EverySale]!, (string)r[BomCol.Notes]!)));

        Assert.Equal(("FG-JUICE", 10m, "Litre"), ((string)rows[0][BomCol.Product]!, D(rows[0][BomCol.Quantity]), (string)rows[0][BomCol.Unit]!));
        Assert.Equal(("RM-MANGO", 20m, "Kilogram"), ((string)rows[1][BomCol.Product]!, D(rows[1][BomCol.Quantity]), (string)rows[1][BomCol.Unit]!));
        Assert.Equal(("BP-PULP", 2m, 10m), ((string)rows[2][BomCol.Product]!, D(rows[2][BomCol.Quantity]), D(rows[2][BomCol.Allocation])));
        Assert.Equal(("Labour", 100m), ((string)rows[3][BomCol.CostTerm]!, D(rows[3][BomCol.Amount])));
        Assert.Null(rows[3][BomCol.Product]);
        Assert.Null(rows[3][BomCol.Unit]);

        // The seeded tenant's line-less BOM still has its Output row.
        Assert.Equal(2, (await ReadAsync(reader, s)).Select(r => r[BomCol.Finished]).Distinct().Count());
    }

    [Fact]
    public async Task A_production_order_is_a_plan_with_no_costs_and_a_journal_carries_what_it_cost()
    {
        var s = await SeedAsync();
        var reader = new ProductionExportReader(s.Db);
        var rows = await ReadAsync(reader, s, new ExportDateRange(new DateOnly(2026, 9, 1), null));
        Assert.All(rows, r => Assert.Equal(reader.Headers.Count, r.Length));

        var order = rows.Where(r => Equals(r[ProdCol.Type], "Production Order")).ToList();
        Assert.Equal(["Output", "Raw Material", "By-Product", "Expense"], order.Select(r => (string)r[ProdCol.Row]!));
        Assert.All(order, r => Assert.Equal(("PRO-0001", "Factory"), ((string)r[ProdCol.Code]!, (string)r[ProdCol.Location]!)));
        Assert.All(order, r => Assert.Null(r[ProdCol.UnitCost]));
        Assert.All(order.Take(3), r => Assert.Null(r[ProdCol.Amount]));
        Assert.All(order, r => Assert.Null(r[ProdCol.Warehouse]));
        Assert.Equal(100m, D(order[3][ProdCol.Amount]));

        var journal = rows.Where(r => Equals(r[ProdCol.Type], "Production Journal")).ToList();
        Assert.Equal(["Output", "Raw Material", "By-Product", "Expense"], journal.Select(r => (string)r[ProdCol.Row]!));
        Assert.All(journal, r => Assert.Equal(("PJ-0001", "PRO-0001", "Main Store A"),
            ((string)r[ProdCol.Code]!, (string)r[ProdCol.AgainstOrder]!, (string)r[ProdCol.Warehouse]!)));

        var (output, raw, byProduct, expense) = (journal[0], journal[1], journal[2], journal[3]);
        Assert.Equal(("FG-JUICE", 10m, "Litre", 45m, 450m),
            ((string)output[ProdCol.Product]!, D(output[ProdCol.Quantity]), (string)output[ProdCol.Unit]!, D(output[ProdCol.UnitCost]), D(output[ProdCol.Amount])));
        Assert.Equal((20m, 20m, 400m), (D(raw[ProdCol.Quantity]), D(raw[ProdCol.UnitCost]), D(raw[ProdCol.Amount])));
        Assert.Equal((2m, 10m, 25m, 50m),
            (D(byProduct[ProdCol.Quantity]), D(byProduct[ProdCol.Allocation]), D(byProduct[ProdCol.UnitCost]), D(byProduct[ProdCol.Amount])));
        Assert.Equal(("Labour", 100m), ((string)expense[ProdCol.CostTerm]!, D(expense[ProdCol.Amount])));

        // What went in is what came out: raw materials and expenses equal the output and the by-product.
        Assert.Equal(D(raw[ProdCol.Amount]) + D(expense[ProdCol.Amount]), D(output[ProdCol.Amount]) + D(byProduct[ProdCol.Amount]));
    }

    [Fact]
    public async Task A_draft_journal_prints_empty_costs_not_zero()
    {
        var s = await SeedAsync();
        var org = s.OrganizationId;
        var juice = await s.Db.Products.SingleAsync(x => x.Code == "FG-JUICE");
        var fruit = await s.Db.Products.SingleAsync(x => x.Code == "RM-MANGO");
        var warehouse = await s.Db.Warehouses.SingleAsync(x => x.OrganizationId == org);
        var draft = ProductionJournal.Create(org, new DateOnly(2026, 9, 10), null, juice.Id, 5m, warehouse.Id, null, null, null, null);
        draft.AddRawMaterial(fruit.Id, 10m);
        s.Db.ProductionJournals.Add(draft);
        await s.Db.SaveChangesAsync();

        var rows = await ReadAsync(new ProductionExportReader(s.Db), s, new ExportDateRange(new DateOnly(2026, 9, 10), null));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(("DRAFT", "Draft"), ((string)r[ProdCol.Code]!, (string)r[ProdCol.Status]!)));
        Assert.All(rows, r => Assert.Null(r[ProdCol.UnitCost]));
        Assert.All(rows, r => Assert.Null(r[ProdCol.Amount]));
        Assert.All(rows, r => Assert.Null(r[ProdCol.AgainstOrder]));
    }

    [Fact]
    public void Every_figure_a_bill_of_materials_or_production_document_stores_is_exported_or_named()
    {
        OrderAndGoodsNoteExportTests.AssertCovered(
            BillOfMaterialsExportReader.ExportedFigures,
            new Dictionary<(Type, string), string>(),
            typeof(BillOfMaterials), typeof(BomRawMaterialLine), typeof(BomByProductLine), typeof(BomExpenseLine));

        const string Sum = "the sum of the journal's rows of that kind, written at Approve";
        OrderAndGoodsNoteExportTests.AssertCovered(
            ProductionExportReader.ExportedFigures,
            new Dictionary<(Type, string), string>
            {
                [(typeof(ProductionJournal), nameof(ProductionJournal.RawMaterialCost))] = Sum,
                [(typeof(ProductionJournal), nameof(ProductionJournal.ProductionExpenseCost))] = Sum,
                [(typeof(ProductionJournal), nameof(ProductionJournal.CostAllocatedToByProduct))] = Sum,
                [(typeof(ProductionJournal), nameof(ProductionJournal.TotalCostOfProduction))] =
                    "computed: the raw materials' and expenses' Amount",
                [(typeof(ProductionJournal), nameof(ProductionJournal.CostRoundingAdjustment))] =
                    "computed: what went in less what came out, the unit cost's rounding residue (phase 25)",
            },
            typeof(ProductionOrder), typeof(ProductionOrderRawMaterialLine), typeof(ProductionOrderByProductLine),
            typeof(ProductionOrderExpenseLine), typeof(ProductionJournal), typeof(ProductionJournalRawMaterialLine),
            typeof(ProductionJournalByProductLine), typeof(ProductionJournalExpenseLine));
    }
}
