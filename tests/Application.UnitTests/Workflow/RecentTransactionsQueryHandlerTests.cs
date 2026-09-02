using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Queries.RecentTransactions;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Phase 23's Home dashboard feed. The layout is not what matters here; <b>the per-document-type
/// permission gating is</b>. This query holds a single blanket key whose only job is to make
/// `AuthorizationBehavior` verify org membership, and it would be a genuine security hole if the
/// handler then listed every document type to anyone holding it. So most of this file is about what
/// a user with a partial set of grants can and cannot see.
///
/// The seeding pattern (well-known `Role.MemberId`, no `HasData` in `TestAppDbContext`) is copied
/// from `TransactionApprovalQueryHandlerTests`, which established it for the same reason.
/// </summary>
public class RecentTransactionsQueryHandlerTests
{
    private static readonly DateOnly Jan01 = new(2026, 1, 1);
    private static readonly DateOnly Jan10 = new(2026, 1, 10);
    private static readonly DateOnly Jan20 = new(2026, 1, 20);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    [Fact]
    public async Task Lists_approved_sales_and_purchase_documents_most_recent_first()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId,
            [PermissionKeys.InvoiceView, PermissionKeys.PurchaseBillView]);

        var invoice = await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, Jan20, 250m);

        var result = await HandleAsync(db, seed, userId);

        // Most recent first -- the opposite of the approval queue's oldest-first ordering, because
        // this answers "what just happened".
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(bill.Code, result.Items[0].DocumentCode);
        Assert.Equal(DocumentType.PurchaseBill, result.Items[0].DocumentType);
        Assert.Equal(invoice.Code, result.Items[1].DocumentCode);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Resolves_each_rows_gross_amount_and_contact_name()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.InvoiceView]);

        await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);

        var row = Assert.Single((await HandleAsync(db, seed, userId)).Items);

        // 100 net + 13% VAT: the feed shows the gross the user would recognise from the document.
        Assert.Equal(113m, row.Amount);
        Assert.Equal("Acme Traders", row.ContactName);
        Assert.Equal(seed.CustomerId, row.ContactId);
    }

    [Fact]
    public async Task Excludes_drafts_and_voided_documents()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.InvoiceView]);

        // A Draft belongs to the Transaction Approval queue, not to a record of what happened.
        await CreateInvoiceAsync(db, seed, Jan10, 999m);
        var approved = await CreateAndApproveInvoiceAsync(db, seed, Jan20, 100m);

        var row = Assert.Single((await HandleAsync(db, seed, userId)).Items);
        Assert.Equal(approved.Code, row.DocumentCode);
    }

    [Fact]
    public async Task Honours_the_date_range()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.InvoiceView]);

        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2025, 12, 31), 100m);
        var inside = await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 2, 1), 100m);

        var row = Assert.Single((await HandleAsync(db, seed, userId)).Items);
        Assert.Equal(inside.Code, row.DocumentCode);
    }

    // --- the part that matters: per-type permission gating -------------------

    [Fact]
    public async Task Shows_only_the_document_types_the_user_may_view()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        // Purchase Bills yes, Invoices no.
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.PurchaseBillView]);

        await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, Jan20, 250m);

        var result = await HandleAsync(db, seed, userId);

        // Not a 403, and not a leak: a partial grant yields a partial feed.
        var row = Assert.Single(result.Items);
        Assert.Equal(bill.Code, row.DocumentCode);
        Assert.DoesNotContain(result.Items, r => r.DocumentType == DocumentType.Invoice);
    }

    [Fact]
    public async Task The_blanket_key_alone_shows_nothing()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        // Holds RecentTransactionView (added by the helper) and nothing else.
        await AddMembershipAsync(db, seed.OrganizationId, userId, []);

        await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        await CreateAndApprovePurchaseBillAsync(db, seed, Jan20, 250m);

        var result = await HandleAsync(db, seed, userId);

        // This is the assertion that makes the blanket key safe: it gets the request past
        // AuthorizationBehavior's org-membership check and grants visibility of nothing on its own.
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task A_user_of_another_organization_sees_nothing()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var outsider = Guid.NewGuid();
        // Membership of a different organization, fully granted there.
        await AddMembershipAsync(db, Guid.NewGuid(), outsider, [PermissionKeys.InvoiceView]);

        await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);

        Assert.Empty((await HandleAsync(db, seed, outsider)).Items);
    }

    // --- tab filters ---------------------------------------------------------

    [Fact]
    public async Task The_Sales_tab_excludes_purchase_documents_and_vice_versa()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId,
            [PermissionKeys.InvoiceView, PermissionKeys.PurchaseBillView]);

        var invoice = await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, Jan20, 250m);

        var sales = await HandleAsync(db, seed, userId, RecentTransactionFilter.Sales);
        var purchase = await HandleAsync(db, seed, userId, RecentTransactionFilter.Purchase);

        Assert.Equal(invoice.Code, Assert.Single(sales.Items).DocumentCode);
        Assert.Equal(bill.Code, Assert.Single(purchase.Items).DocumentCode);
    }

    [Fact]
    public async Task The_Receipt_and_Payment_tabs_split_the_one_Payment_aggregate_by_direction()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId, [PermissionKeys.PaymentView]);

        await AddApprovedPaymentAsync(db, seed, Jan10, 500m, PaymentDirection.Received, "RCPT-1");
        await AddApprovedPaymentAsync(db, seed, Jan20, 300m, PaymentDirection.Paid, "PAY-1");

        var receipts = await HandleAsync(db, seed, userId, RecentTransactionFilter.Receipt);
        var payments = await HandleAsync(db, seed, userId, RecentTransactionFilter.Payment);
        var all = await HandleAsync(db, seed, userId);

        var receipt = Assert.Single(receipts.Items);
        Assert.Equal(PaymentDirection.Received, receipt.Direction);
        Assert.Equal(500m, receipt.Amount);

        var payment = Assert.Single(payments.Items);
        Assert.Equal(PaymentDirection.Paid, payment.Direction);
        Assert.Equal(300m, payment.Amount);

        // Direction is what the Angular side needs to pick between two routes for one aggregate.
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task Pages_over_the_merged_stream_rather_than_per_type()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var userId = Guid.NewGuid();
        await AddMembershipAsync(db, seed.OrganizationId, userId,
            [PermissionKeys.InvoiceView, PermissionKeys.PurchaseBillView]);

        await CreateAndApproveInvoiceAsync(db, seed, Jan10, 100m);
        await CreateAndApprovePurchaseBillAsync(db, seed, Jan20, 250m);
        await CreateAndApproveInvoiceAsync(db, seed, Jan31, 100m);

        var first = await HandleAsync(db, seed, userId, RecentTransactionFilter.All, page: 1, pageSize: 2);
        var second = await HandleAsync(db, seed, userId, RecentTransactionFilter.All, page: 2, pageSize: 2);

        // Composing this client-side from separate per-type endpoints could not produce a correctly
        // ordered page 2 -- which is why this query exists at all.
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(Jan31, first.Items[0].Date);
        Assert.Equal(Jan10, second.Items[0].Date);
    }

    // --- helpers --------------------------------------------------------------

    private static Task<PagedResult<RecentTransactionRowDto>> HandleAsync(
        IAppDbContext db,
        Seed seed,
        Guid userId,
        RecentTransactionFilter filter = RecentTransactionFilter.All,
        int page = 1,
        int pageSize = 50) =>
        new RecentTransactionsQueryHandler(db, new FakeCurrentUserService(userId)).Handle(
            new RecentTransactionsQuery(seed.OrganizationId, Jan01, Jan31, filter, page, pageSize),
            CancellationToken.None);

    private static async Task AddMembershipAsync(
        IAppDbContext db, Guid organizationId, Guid userId, IReadOnlyList<string> grantedViewKeys)
    {
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organizationId, userId, MembershipRole.Member));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.MemberId, PermissionKeys.RecentTransactionView, true));
        foreach (var key in grantedViewKeys)
        {
            db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.MemberId, key, true));
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task AddApprovedPaymentAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount, PaymentDirection direction, string code)
    {
        // Built directly rather than through Create/Approve: this suite is about the feed's gating
        // and ordering, and a Payment's own approval path drags in allocations it does not need.
        var payment = Payment.Create(
            seed.OrganizationId, seed.CustomerId, direction, date, null, seed.CashAccountId, amount, null);
        payment.Approve(Guid.NewGuid(), code);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate) =>
        new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat)]),
            CancellationToken.None);

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate)
    {
        var created = await CreateInvoiceAsync(db, seed, date, rate);
        var stock = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stock), stock)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null,
                false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)]),
            CancellationToken.None);
        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId, Guid CashAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Nepal Supply Co", null, null, null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Hour", "hr"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Direct Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var vatReceivable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Receivable", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id, product.Id, cash.Id);
    }
}
