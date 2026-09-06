using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// The smallest tenant that can approve a Service-line invoice: a customer, a Service product, a
/// warehouse, and the three sales GL defaults. Service, not Goods, deliberately -- phase 30's E2E
/// found that a Goods line consumes stock regardless of TrackInventory, so a Goods product cannot be
/// invoiced without seeding a FIFO layer first, and none of these tests are about stock.
/// </summary>
internal sealed record CreditSeed(
    Guid OrganizationId,
    FakeDocumentNumberGenerator NumberGenerator,
    Guid CustomerId,
    Guid WarehouseId,
    Guid ProductId);

internal static class CreditLimitTestSeed
{
    internal static async Task<CreditSeed> SeedAsync(
        IAppDbContext db,
        decimal creditLimit = 0m,
        BalanceAction creditLimitExceedsAction = BalanceAction.Warn,
        decimal openingBalance = 0m,
        decimal sellingPrice = 150m,
        VatRate vatRate = VatRate.NoVat,
        SuggestSellingPriceMode suggestMode = SuggestSellingPriceMode.RecentSellingPrice,
        ProductPriceBasis priceBasis = ProductPriceBasis.ExclusiveOfVat)
    {
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, openingBalance,
                CreditLimit: creditLimit),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true,
                sellingPrice, 100m, vatRate, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", Domain.Accounting.AccountRootType.Asset, null),
            CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", Domain.Accounting.AccountRootType.Liability, null),
            CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", Domain.Accounting.AccountRootType.Income, null),
            CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, null, null, null);
        settings.UpdateSettings(
            suggestMode, priceBasis, InventoryTrackingMode.AccountingMovement,
            BalanceAction.Reject, BalanceAction.Warn, creditLimitExceedsAction);
        db.TenantSettings.Add(settings);

        await db.SaveChangesAsync(CancellationToken.None);

        return new CreditSeed(organizationId, numbers, customer.Id, warehouse.Id, product.Id);
    }
}
