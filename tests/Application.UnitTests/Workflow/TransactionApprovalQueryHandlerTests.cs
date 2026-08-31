using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Queries.TransactionApproval;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Covers roadmap Phase 12's TransactionApprovalQuery -- the first test suite for the Workflow
/// bounded context. The behavior these tests exist to prove is the permission-based per-document-
/// type filtering (see TransactionApprovalQuery's own doc comment): a Draft document only shows up
/// if the querying user's Role actually holds that specific document type's own *.Approve grant,
/// not merely PermissionKeys.TransactionApprovalView.
/// </summary>
public class TransactionApprovalQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_only_draft_rows_across_multiple_document_types_and_excludes_approved()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(
            db, seed.OrganizationId, userId,
            [
                PermissionKeys.QuotationApprove, PermissionKeys.PurchaseOrderApprove,
                PermissionKeys.JournalVoucherApprove, PermissionKeys.WarehouseTransferApprove,
                PermissionKeys.InvoiceApprove,
            ]);

        var quotation = await CreateQuotationAsync(db, seed);
        var purchaseOrder = await CreatePurchaseOrderAsync(db, seed);
        var invoice = await CreateInvoiceAsync(db, seed);
        var warehouseTransfer = await CreateWarehouseTransferAsync(db, seed);

        // Created and then Approved -- must NOT appear, even though its document type's grant is held.
        var approvedJournalVoucherId = await CreateAndApproveJournalVoucherAsync(db, seed);

        // A second JournalVoucher left in Draft -- must appear.
        var draftJournalVoucher = await CreateJournalVoucherAsync(db, seed);

        var handler = new TransactionApprovalQueryHandler(db, new FakeCurrentUserService(userId));
        var result = await handler.Handle(new TransactionApprovalQuery(seed.OrganizationId), CancellationToken.None);

        Assert.Equal(5, result.Rows.Count);
        Assert.DoesNotContain(result.Rows, r => r.DocumentId == approvedJournalVoucherId);

        var quotationRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.Quotation);
        Assert.Equal(quotation.Id, quotationRow.DocumentId);
        Assert.Equal(seed.CustomerId, quotationRow.ContactId);
        Assert.Equal(seed.CustomerName, quotationRow.ContactName);

        var purchaseOrderRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.PurchaseOrder);
        Assert.Equal(purchaseOrder.Id, purchaseOrderRow.DocumentId);
        Assert.Equal(seed.SupplierId, purchaseOrderRow.ContactId);

        var invoiceRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.Invoice);
        Assert.Equal(invoice.Id, invoiceRow.DocumentId);

        var warehouseTransferRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.WarehouseTransfer);
        Assert.Equal(warehouseTransfer.Id, warehouseTransferRow.DocumentId);
        Assert.Null(warehouseTransferRow.ContactId);
        Assert.Null(warehouseTransferRow.ContactName);

        var journalVoucherRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.JournalVoucher);
        Assert.Equal(draftJournalVoucher.Id, journalVoucherRow.DocumentId);
    }

    [Fact]
    public async Task Handle_excludes_a_document_type_entirely_when_the_users_role_lacks_that_types_approve_permission()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();

        // Granted Invoice.Approve but NOT PurchaseOrder.Approve -- this is the exact behavior the
        // whole feature exists for (FR-10.2), tested directly rather than only via the happy path.
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.InvoiceApprove]);

        var invoice = await CreateInvoiceAsync(db, seed);
        await CreatePurchaseOrderAsync(db, seed); // Draft, but must not appear -- no PurchaseOrder.Approve grant.

        var handler = new TransactionApprovalQueryHandler(db, new FakeCurrentUserService(userId));
        var result = await handler.Handle(new TransactionApprovalQuery(seed.OrganizationId), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(DocumentType.Invoice, row.DocumentType);
        Assert.Equal(invoice.Id, row.DocumentId);
    }

    [Fact]
    public async Task Handle_never_returns_a_draft_from_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var seedA = await SeedAsync(db);
        var seedB = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seedA.OrganizationId, userId, [PermissionKeys.QuotationApprove]);

        var quotationA = await CreateQuotationAsync(db, seedA);
        await CreateQuotationAsync(db, seedB); // Different Organization -- must never appear for userId's org-A query.

        var handler = new TransactionApprovalQueryHandler(db, new FakeCurrentUserService(userId));
        var result = await handler.Handle(new TransactionApprovalQuery(seedA.OrganizationId), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(quotationA.Id, row.DocumentId);
    }

    private sealed record Seed(
        Guid OrganizationId, Guid CustomerId, string CustomerName, Guid SupplierId, Guid ServiceProductId,
        Guid GoodsProductId, Guid Warehouse1Id, Guid Warehouse2Id, Guid AssetAccountId, Guid LiabilityAccountId,
        FakeDocumentNumberGenerator NumberGenerator);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        const string customerName = "Acme Retail";
        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, customerName, null, null, null, null, null, 0m),
            CancellationToken.None);

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Hour", "hr"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        // WarehouseTransferLineInput requires a Goods-type product (InventoryValidation.
        // EnsureProductsAreGoodsAsync rejects a Service product), so this needs its own product.
        var goodsProduct = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 50m, 30m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);

        // Phase 20f: a second warehouse needs the MultipleWarehouses entitlement.
        await TenantFeatureSeed.SeedAllFeaturesEnabledAsync(db, organizationId);

        var warehouse1 = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var warehouse2 = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Secondary Warehouse"), CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var assetAccount = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var liabilityAccount = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);

        return new Seed(
            organizationId, customer.Id, customerName, supplier.Id, product.Id, goodsProduct.Id, warehouse1.Id,
            warehouse2.Id, assetAccount.Id, liabilityAccount.Id, numberGenerator);
    }

    private static async Task AddMembershipAsync(
        IAppDbContext db, Guid organizationId, Guid userId, IReadOnlyList<string> grantedApproveKeys)
    {
        // Reuses the well-known system Member RoleId (Role.MemberId) -- TestAppDbContext has no
        // HasData seed (see its own doc comment), so this test fully controls what's granted for
        // it, same pattern AuthorizationBehaviorTests already established.
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organizationId, userId, MembershipRole.Member));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.MemberId, PermissionKeys.TransactionApprovalView, true));
        foreach (var key in grantedApproveKeys)
        {
            db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.MemberId, key, true));
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<CreateQuotationResult> CreateQuotationAsync(IAppDbContext db, Seed seed) =>
        new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(
                seed.OrganizationId, seed.CustomerId, new DateOnly(2026, 1, 10), null, null,
                [new QuotationLineInput(seed.ServiceProductId, 1m, 100m, VatRate.NoVat)]),
            CancellationToken.None);

    private static Task<CreatePurchaseOrderResult> CreatePurchaseOrderAsync(IAppDbContext db, Seed seed) =>
        new CreatePurchaseOrderCommandHandler(db).Handle(
            new CreatePurchaseOrderCommand(
                seed.OrganizationId, seed.SupplierId, new DateOnly(2026, 1, 10), null,
                [new PurchaseOrderLineInput(seed.ServiceProductId, 1m, 80m, VatRate.NoVat)]),
            CancellationToken.None);

    private static Task<CreateInvoiceResult> CreateInvoiceAsync(IAppDbContext db, Seed seed) =>
        new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.Warehouse1Id, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ServiceProductId, 1m, 100m, VatRate.NoVat)]),
            CancellationToken.None);

    private static Task<CreateWarehouseTransferResult> CreateWarehouseTransferAsync(IAppDbContext db, Seed seed) =>
        new CreateWarehouseTransferCommandHandler(db).Handle(
            new CreateWarehouseTransferCommand(
                seed.OrganizationId, seed.Warehouse1Id, seed.Warehouse2Id, new DateOnly(2026, 1, 10), null,
                [new WarehouseTransferLineInput(seed.GoodsProductId, 1m)]),
            CancellationToken.None);

    private static Task<CreateJournalVoucherResult> CreateJournalVoucherAsync(IAppDbContext db, Seed seed) =>
        new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                seed.OrganizationId, new DateOnly(2026, 1, 10), null,
                [
                    new JournalVoucherLineInput(seed.AssetAccountId, 500m, 0m),
                    new JournalVoucherLineInput(seed.LiabilityAccountId, 0m, 500m),
                ]),
            CancellationToken.None);

    private static async Task<Guid> CreateAndApproveJournalVoucherAsync(IAppDbContext db, Seed seed)
    {
        var created = await CreateJournalVoucherAsync(db, seed);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return created.Id;
    }
}
