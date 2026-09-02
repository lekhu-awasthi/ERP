using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// The one rule Phase 24 had to sweep for: a variant *parent* may never reach a document line.
/// See ProductVariantRules for why (a parent stock bucket reconciles against nothing), and
/// ProductVariantSweepGuardTests for the proof that the four call sites are all the seams there are.
///
/// The rule itself is asserted directly against the public ProductVariantRules; the *wiring* is
/// asserted through real Create handlers, one per module, because a rule nothing calls is worth
/// nothing -- which is precisely what phase-23's bug #1 turned out to be.
/// </summary>
public class ProductVariantTransactabilityTests
{
    private static readonly Guid ColorId = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();

    private sealed record Fixture(
        IAppDbContext Db, Guid OrganizationId, Guid ParentId, Guid VariantId, Guid OrdinaryId,
        Guid WarehouseId, Guid CustomerId, Guid SupplierId);

    private static async Task<Fixture> SeedAsync()
    {
        var db = TestAppDbContext.Create();

        var organization = Organization.Create(
            "Acme", "Retail", null, new DateOnly(2026, 1, 1), true, "acme", null, null, null, null, Guid.NewGuid());
        var orgId = organization.Id;

        var parent = Product.Create(
            orgId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 500m, 300m, VatRate.ThirteenPercentVat, 0, true);
        parent.SetVariantAttributeUsages([(ColorId, Blue)]);

        var variant = parent.CreateVariant("P-0002", "T-Shirt Blue", [(ColorId, Blue)], 500m, 300m, null, null);

        var ordinary = Product.Create(
            orgId, ProductType.Goods, "Mug", "P-0003", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 100m, 50m, VatRate.ThirteenPercentVat, 0, true);

        var warehouse = Warehouse.Create(orgId, "Main");
        var customer = Contact.Create(orgId, ContactType.Customer, "Acme Buyer", "C-0001", null, null, null, null, null, 0m);
        var supplier = Contact.Create(orgId, ContactType.Supplier, "Supplies Ltd", "C-0002", null, null, null, null, null, 0m);

        db.Organizations.Add(organization);
        db.Products.AddRange(parent, variant, ordinary);
        db.Warehouses.Add(warehouse);
        db.Contacts.AddRange(customer, supplier);
        await db.SaveChangesAsync();

        return new Fixture(db, orgId, parent.Id, variant.Id, ordinary.Id, warehouse.Id, customer.Id, supplier.Id);
    }

    // ---- the rule itself ----

    [Fact]
    public async Task The_rule_rejects_a_variant_parent()
    {
        var f = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
                f.Db, f.OrganizationId, [f.ParentId], CancellationToken.None));

        Assert.Contains("has variants", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_parent_hidden_among_valid_products_is_still_rejected()
    {
        // The check must inspect every submitted id, not just the first -- a multi-line document is
        // exactly how a parent would otherwise slip through.
        var f = await SeedAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
                f.Db, f.OrganizationId, [f.OrdinaryId, f.VariantId, f.ParentId], CancellationToken.None));
    }

    [Fact]
    public async Task Variants_and_ordinary_products_are_both_accepted()
    {
        // The regression that matters: every existing tenant holds only ordinary products.
        var f = await SeedAsync();

        await ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
            f.Db, f.OrganizationId, [f.OrdinaryId, f.VariantId], CancellationToken.None);
    }

    [Fact]
    public async Task A_missing_product_is_still_a_404_not_a_409()
    {
        // The pre-Phase-24 behaviour, unchanged: existence is checked before transactability, so a
        // caller still cannot tell a nonexistent id from a parent.
        var f = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
                f.Db, f.OrganizationId, [Guid.NewGuid()], CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_line_set_is_a_no_op()
    {
        var f = await SeedAsync();

        await ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
            f.Db, f.OrganizationId, [], CancellationToken.None);
    }

    // ---- the wiring, through real handlers ----

    [Fact]
    public async Task CreateInvoice_rejects_a_variant_parent_but_accepts_its_variant()
    {
        var f = await SeedAsync();
        var handler = new CreateInvoiceCommandHandler(f.Db);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(Invoice(f, f.ParentId), CancellationToken.None));

        var ok = await handler.Handle(Invoice(f, f.VariantId), CancellationToken.None);
        Assert.NotEqual(Guid.Empty, ok.Id);
    }

    [Fact]
    public async Task CreatePurchaseBill_rejects_a_variant_parent_but_accepts_its_variant()
    {
        var f = await SeedAsync();
        var handler = new CreatePurchaseBillCommandHandler(f.Db);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(Bill(f, f.ParentId), CancellationToken.None));

        var ok = await handler.Handle(Bill(f, f.VariantId), CancellationToken.None);
        Assert.NotEqual(Guid.Empty, ok.Id);
    }

    [Fact]
    public async Task CreateOrUpdateOpeningStockLine_rejects_a_variant_parent_but_accepts_its_variant()
    {
        var f = await SeedAsync();
        var handler = new CreateOrUpdateOpeningStockLineCommandHandler(f.Db, new StockLedgerService(f.Db));

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(
                new CreateOrUpdateOpeningStockLineCommand(f.OrganizationId, f.ParentId, f.WarehouseId, 5m, 100m),
                CancellationToken.None));

        var ok = await handler.Handle(
            new CreateOrUpdateOpeningStockLineCommand(f.OrganizationId, f.VariantId, f.WarehouseId, 5m, 100m),
            CancellationToken.None);
        Assert.Equal(f.VariantId, ok.ProductId);
    }

    private static CreateInvoiceCommand Invoice(Fixture f, Guid productId) =>
        new(f.OrganizationId, f.CustomerId, f.WarehouseId, new DateOnly(2026, 1, 1), null,
            [new InvoiceLineInput(productId, 1m, 500m, VatRate.ThirteenPercentVat)]);

    private static CreatePurchaseBillCommand Bill(Fixture f, Guid productId) =>
        new(f.OrganizationId, f.SupplierId, f.WarehouseId, new DateOnly(2026, 1, 1), null, null,
            false, null, null, null, null,
            [new PurchaseBillLineInput(
                productId, 1m, 300m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)]);
}
