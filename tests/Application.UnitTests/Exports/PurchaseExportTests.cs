using System.Reflection;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Readers;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// Phase 78 -- the Purchase Documents sheet carries every part of a purchase's money: expenses, TDS and additional
/// costs as rows of their own, a debit note's bill and the classification it inherits from it, and the unit, rate and
/// location every row needed.
/// </summary>
public sealed class PurchaseExportTests
{
    // Column positions: phase 38's fourteen, then phase 78's twelve, then phase 80's Allocation Method.
    private static class Col
    {
        public const int Type = 0, ProductCode = 7, Quantity = 9, Amount = 12, Vat = 13, AgainstBill = 14,
        SupplierInvoiceRef = 15, Account = 16, Classification = 17, Import = 18, DocumentDiscount = 19, Tds = 20,
        CostTerm = 21, AdditionalCost = 22, Unit = 23, ExchangeRate = 24, Location = 25, AllocationMethod = 26;
    }

    private sealed record Seeded(
        Guid OrganizationId, PurchaseBill ForeignBill, DebitNote Return, DebitNote Standalone, Expense Rent,
        string RentAccount, string LocationName, IAppDbContext Db);

    private static async Task<Seeded> SeedAsync()
    {
        var db = TestAppDbContext.Create(Guid.NewGuid().ToString());
        var tenant = await ExportTestSeed.SeedAsync(db);
        var org = tenant.OrganizationId;
        var supplier = await db.Contacts.SingleAsync(x => x.Code == "S-A-0001");
        var product = await db.Products.SingleAsync(x => x.Code == tenant.ProductCode);
        var warehouse = await db.Warehouses.SingleAsync(x => x.OrganizationId == org);

        var carton = UnitOfMeasurement.Create(org, "Carton", "ctn");
        var location = BillingLocation.Create(org, "BR1", "Pokhara Branch", null, null);
        var freight = CostTerm.Create(org, "Freight", CostTermCategory.AdditionalCost);
        var group = await db.AccountGroups.FirstAsync(x => x.OrganizationId == org);
        var rentAccount = Account.Create(org, "EX-01", "Rent", AccountRootType.Expense, group.Id);
        db.UnitsOfMeasurement.Add(carton);
        db.BillingLocations.Add(location);
        db.CostTerms.Add(freight);
        db.Accounts.Add(rentAccount);

        // An imported bill in dollars at a branch: 4 cartons at 100, 5% off the line and 10% off the bill (342),
        // 15 TDS withheld, and 50 of freight that is not the supplier's.
        var bill = PurchaseBill.Create(
            org, supplier.Id, warehouse.Id, new DateOnly(2026, 8, 25), "REF-B2", "SUP-INV-9", true, "India",
            new DateOnly(2026, 8, 24), "IMP-1", null, 15m, null, null, discountPct: 10m);
        bill.SetCurrency("USD", 133.5m);
        bill.SetLocation(location.Id);
        bill.AddLine(product.Id, 4m, 100m, VatRate.ThirteenPercentVat, ExpenditureClassification.Capital, 5m, carton.Id, 12m, null, VatCreditUse.Taxable, VatCreditCategory.Full);
        bill.AddAdditionalCost(freight.Id, null, AdditionalCostMethod.Value, 50m);
        db.PurchaseBills.Add(bill);

        // One carton back against it (85.50), giving back 3 of the TDS.
        var debitNote = DebitNote.Create(
            org, supplier.Id, new DateOnly(2026, 8, 26), "REF-DN", null, 3m, DocumentType.PurchaseBill, bill.Id, 10m);
        debitNote.SetCurrency("USD", 133.5m);
        debitNote.AddLine(product.Id, 1m, 100m, VatRate.ThirteenPercentVat, 5m, carton.Id, 12m);
        db.DebitNotes.Add(debitNote);

        var standalone = DebitNote.Create(org, supplier.Id, new DateOnly(2026, 8, 26), "REF-DN2", null, 0m, null, null);
        standalone.AddLine(product.Id, 2m, 80m, VatRate.ThirteenPercentVat, 0m, null, 1m);
        db.DebitNotes.Add(standalone);

        var expense = Expense.Create(org, supplier.Id, new DateOnly(2026, 8, 27), null, "SUP-EXP-1", "August rent", true, null, 20m);
        expense.AddLine(rentAccount.Id, 2000m, VatRate.ThirteenPercentVat, VatCreditUse.Taxable, VatCreditCategory.Full);
        expense.SetLocation(location.Id);
        db.Expenses.Add(expense);

        await db.SaveChangesAsync();
        return new Seeded(org, bill, debitNote, standalone, expense, "EX-01 Rent", location.Name, db);
    }

    private static async Task<IReadOnlyList<object?[]>> SheetAsync(Seeded s, ExportDateRange? range = null) =>
        (await new PurchaseDocumentExportReader(s.Db).ReadAsync(
            s.OrganizationId, 1000, range ?? ExportDateRange.Unbounded, CancellationToken.None)).Rows;

    private static decimal D(object? cell) => (decimal)cell!;

    [Fact]
    public async Task A_bill_prints_its_lines_then_its_additional_cost_then_its_TDS()
    {
        var s = await SeedAsync();
        var rows = await SheetAsync(s);
        Assert.All(rows, r => Assert.Equal(new PurchaseDocumentExportReader(s.Db).Headers.Count, r.Length));

        var bill = rows.Where(r => Equals(r[Col.Type], "Purchase Bill") && Equals(r[Col.Location], s.LocationName)).ToList();
        Assert.Equal(3, bill.Count);

        var line = bill[0];
        Assert.Equal((4m, 342m, 44.46m), (D(line[Col.Quantity]), D(line[Col.Amount]), D(line[Col.Vat])));
        Assert.Equal(("Carton", 133.5m, "Capital", "Yes", 10m, "SUP-INV-9"),
            ((string)line[Col.Unit]!, D(line[Col.ExchangeRate]), (string)line[Col.Classification]!, (string)line[Col.Import]!,
             D(line[Col.DocumentDiscount]), (string)line[Col.SupplierInvoiceRef]!));

        // The freight is a row of its own, never in Amount: the supplier is credited only the goods.
        var cost = bill[1];
        Assert.Null(cost[Col.ProductCode]);
        Assert.Equal(("Freight", 50m, 0m), ((string)cost[Col.CostTerm]!, D(cost[Col.AdditionalCost]), D(cost[Col.Amount])));
        Assert.Null(cost[Col.Unit]);

        // Phase 80 -- how the freight was spread over the lines, on its own row and no other.
        Assert.Equal("Value", cost[Col.AllocationMethod]);
        Assert.Null(line[Col.AllocationMethod]);

        var tds = bill[2];
        Assert.Equal((15m, 0m, 0m), (D(tds[Col.Tds]), D(tds[Col.Amount]), D(tds[Col.Vat])));
        Assert.Equal("Yes", tds[Col.Import]);
    }

    [Fact]
    public async Task A_debit_note_names_its_bill_and_inherits_its_lines_classification_as_the_registers_do()
    {
        var s = await SeedAsync();
        var rows = await SheetAsync(s);

        var returned = rows.Where(r => Equals(r[Col.Type], "Debit Note") && Equals(r[Col.AgainstBill], s.ForeignBill.Code)).ToList();
        var line = Assert.Single(returned, r => r[Col.ProductCode] is not null);
        Assert.Equal((-1m, -85.50m, "Capital", "Yes", "Carton"),
            (D(line[Col.Quantity]), D(line[Col.Amount]), (string)line[Col.Classification]!, (string)line[Col.Import]!, (string)line[Col.Unit]!));
        Assert.Equal(-3m, D(Assert.Single(returned, r => r[Col.ProductCode] is null)[Col.Tds]));

        // A note naming no bill classifies as the Purchase Register files it: Others, local.
        var standalone = Assert.Single(rows, r => Equals(r[Col.Type], "Debit Note") && D(r[Col.Quantity]) == -2m);
        Assert.Equal((null, "Others", "No"), (standalone[Col.AgainstBill], (string)standalone[Col.Classification]!, (string)standalone[Col.Import]!));
        Assert.Equal("Box A", standalone[Col.Unit]);
    }

    [Fact]
    public async Task An_expense_is_a_purchase_whose_line_names_an_account()
    {
        var s = await SeedAsync();
        var rows = (await SheetAsync(s)).Where(r => Equals(r[Col.Type], "Expense")).ToList();

        Assert.Equal(2, rows.Count);
        var line = rows[0];
        Assert.Equal((s.RentAccount, 2000m, 260m, "SUP-EXP-1", s.LocationName),
            ((string)line[Col.Account]!, D(line[Col.Amount]), D(line[Col.Vat]), (string)line[Col.SupplierInvoiceRef]!, (string)line[Col.Location]!));
        Assert.Null(line[Col.ProductCode]);
        Assert.Null(line[Col.Classification]);
        Assert.Null(line[Col.Import]);
        Assert.Equal(20m, D(rows[1][Col.Tds]));
        Assert.Null(rows[1][Col.Account]);
    }

    [Fact]
    public async Task What_the_suppliers_are_owed_is_the_sheets_amount_plus_VAT_less_TDS()
    {
        var s = await SeedAsync();
        var rows = await SheetAsync(s);
        var db = s.Db;

        var bills = await db.PurchaseBills.Include(x => x.Lines).Where(x => x.OrganizationId == s.OrganizationId).ToListAsync();
        var notes = await db.DebitNotes.Include(x => x.Lines).Where(x => x.OrganizationId == s.OrganizationId).ToListAsync();
        var expenses = await db.Expenses.Include(x => x.Lines).Where(x => x.OrganizationId == s.OrganizationId).ToListAsync();
        var owed = bills.Sum(x => x.GrandTotal - x.TdsAmount)
                   - notes.Sum(x => x.Lines.Sum(l => l.Amount + l.VatAmount) - x.TdsAmount)
                   + expenses.Sum(x => x.GrandTotal - x.TdsAmount);

        Assert.Equal(owed, rows.Sum(r => D(r[Col.Amount]) + D(r[Col.Vat]) - D(r[Col.Tds])));
        Assert.Equal(50m, rows.Sum(r => D(r[Col.AdditionalCost])));

        // The seeded base-currency bill at no location: rate 1, the product's own unit.
        var plain = Assert.Single(rows, r => Equals(r[Col.Type], "Purchase Bill") && D(r[Col.Quantity]) == 5m);
        Assert.Equal(("Box A", 1m, null, "Others", "No"),
            ((string)plain[Col.Unit]!, D(plain[Col.ExchangeRate]), plain[Col.Location], (string)plain[Col.Classification]!, (string)plain[Col.Import]!));
    }

    [Fact]
    public async Task The_period_cuts_every_kind_of_row_by_its_documents_date()
    {
        var s = await SeedAsync();
        var rows = await SheetAsync(s, new ExportDateRange(new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 25)));

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("Purchase Bill", r[Col.Type]));
    }

    /// <summary>
    /// Every <c>decimal</c> a purchase document or its rows store is either on the sheet or named here with the
    /// reason it is not, as phase 77 holds the sales lines.
    /// </summary>
    [Fact]
    public void Every_decimal_a_purchase_document_stores_is_exported_or_named()
    {
        var notExported = new Dictionary<(System.Type, string), string>
        {
            [(typeof(PurchaseBillLine), nameof(PurchaseBillLine.ConversionFactor))] = "a unit's factor, not money (phase 52)",
            [(typeof(DebitNoteLine), nameof(DebitNoteLine.ConversionFactor))] = "a unit's factor, not money (phase 52)",
            [(typeof(DebitNoteLine), nameof(DebitNoteLine.ConsumedUnitCost))] = "a cost, not a price: Stock Movements carries the value",
            [(typeof(PurchaseBill), nameof(PurchaseBill.CapitalisedAdditionalCost))] = "base-currency stock value written at Approve: Stock Movements",
            [(typeof(PurchaseBill), nameof(PurchaseBill.AdditionalCostRoundingAdjustment))] = "the capitalisation's residue, in the ledger",
            [(typeof(PurchaseBill), nameof(PurchaseBill.GrandTotal))] = "derived: Amount + VAT Amount",
            [(typeof(PurchaseBill), nameof(PurchaseBill.AdditionalCostTotal))] = "derived: the sum of Additional Cost",
            [(typeof(Expense), nameof(Expense.GrandTotal))] = "derived: Amount + VAT Amount",
            [(typeof(PurchaseBillAdditionalCostAllocation), nameof(PurchaseBillAdditionalCostAllocation.Amount))] =
                "derived at Approve from the Additional Cost rows the sheet carries",
            [(typeof(PurchaseBillLine), nameof(PurchaseBillLine.DisallowedVat))] =
                "derived (phase 99): VAT Amount and the VAT Credit Use and Category columns",
            [(typeof(ExpenseLine), nameof(ExpenseLine.DisallowedVat))] =
                "derived (phase 99): VAT Amount and the VAT Credit Use and Category columns",
        };

        System.Type[] types =
        [
            typeof(PurchaseBill), typeof(PurchaseBillLine), typeof(DebitNote), typeof(DebitNoteLine), typeof(Expense),
            typeof(ExpenseLine), typeof(PurchaseBillAdditionalCost), typeof(PurchaseBillAdditionalCostAllocation),
        ];

        foreach (var type in types)
        {
            var decimals = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Select(p => (type, p.Name))
                .ToList();

            Assert.NotEmpty(decimals);
            Assert.All(decimals, key => Assert.True(
                PurchaseDocumentExportReader.ExportedMoney.Contains(key) || notExported.ContainsKey(key),
                $"{key.type.Name}.{key.Name} is neither exported on the Purchase Documents sheet nor named."));
        }

        // Both lists name only what exists, or a rename would leave a stale entry passing for coverage.
        Assert.All(PurchaseDocumentExportReader.ExportedMoney.Concat(notExported.Keys),
            key => Assert.NotNull(key.Item1.GetProperty(key.Item2)));
    }
}
