using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Tenancy;
using ErpApp.Application.Tenancy.Commands.SetTenantSubscription;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 41 -- <c>SubscriptionQuotaBehavior</c> and the reader behind it.
///
/// <para>The figures in these tests are the published ones (tiggapp.com/pricing): Basic sells 30,000
/// transactions and 1,000 products a year, Standard 50,000 and 5,000, Professional 200,000 and
/// 10,000. The tests use small ceilings for speed, but a ceiling of <c>0</c> is always the real
/// sentinel under test, because it is what every trial and every pre-phase-41 tenant carries.</para>
/// </summary>
public class SubscriptionQuotaTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task A_tenant_with_no_ceiling_is_not_metered_and_the_count_never_runs()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 0, productQuota: 0);

        // Far more posted entries than any ceiling under test: if zero were read as a ceiling rather
        // than as "no limit", this is the shape that would brick every trial on its first invoice.
        await PostEntriesAsync(db, 25);

        Assert.Equal(0, await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, DateTimeOffset.UtcNow, default));
        await ApproveAsync(db);
    }

    [Fact]
    public async Task Usage_counts_distinct_documents_not_gl_entries()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 10, productQuota: 0);

        var invoiceId = Guid.NewGuid();

        // The approve, then the void's reversing entry against the same source document -- phase 16a's
        // shape, and phase 36's point that a document can carry several entries. One transaction was
        // affected, so one unit is consumed. Counting rows would charge two.
        await PostEntryAsync(db, DocumentType.Invoice, invoiceId);
        await PostEntryAsync(db, DocumentType.Invoice, invoiceId);

        Assert.Equal(1, await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task An_unmetered_document_type_does_not_consume_the_allowance()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 10, productQuota: 0);

        await PostEntryAsync(db, DocumentType.Invoice, Guid.NewGuid());

        // Opening balances are setup, not trade; they post GL entries and are deliberately outside
        // DocumentMechanisms.MeteredTransactions. A tenant must not spend its annual allowance on
        // entering its own opening position.
        await PostEntryAsync(db, DocumentType.OpeningBalance, Guid.NewGuid());
        await PostEntryAsync(db, DocumentType.OpeningStock, Guid.NewGuid());

        Assert.Equal(1, await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task Only_the_current_term_counts()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 10, productQuota: 0);

        // A transaction from the term before this one. The allowance is per term -- the price list
        // sells "transactions / year" -- so a renewal has to reopen it, which is exactly what
        // TermStartsAt exists to express.
        await PostEntryAsync(db, DocumentType.Invoice, Guid.NewGuid(), subscription.TermStartsAt.AddDays(-5));
        await PostEntryAsync(db, DocumentType.Invoice, Guid.NewGuid());

        Assert.Equal(1, await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task Another_tenants_transactions_do_not_count()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 10, productQuota: 0);

        await PostEntryAsync(db, DocumentType.Invoice, Guid.NewGuid(), organizationId: Guid.NewGuid());

        Assert.Equal(0, await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task The_last_transaction_inside_the_ceiling_is_allowed_and_the_next_is_refused()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 3, productQuota: 0);

        await PostEntriesAsync(db, 2);
        await ApproveAsync(db);

        await PostEntriesAsync(db, 1);

        // Three used against a ceiling of three: the request that would make it four is the one to
        // refuse, which is what makes the comparison >= rather than >.
        var error = await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(() => ApproveAsync(db));

        Assert.Equal(SubscriptionQuotaKind.Transactions, error.Kind);
        Assert.Equal(3, error.Used);
        Assert.Equal(3, error.Quota);
        Assert.Contains("Standard", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_ceiling_refuses_the_next_product()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 0, productQuota: 1);

        var behavior = new SubscriptionQuotaBehavior<
            ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductCommand,
            ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductResult>(db, TimeProvider.System);

        var command = new ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductCommand(
            OrganizationId, ProductType.Service, "Consulting", Guid.NewGuid(),
            Guid.NewGuid(), null, true, 100m, 80m, VatRate.NoVat, 0, false);

        var result = new ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductResult(
            Guid.NewGuid(), "P0001", ProductType.Service, "Consulting");

        // Nothing created yet: under the ceiling, so it passes straight through.
        Assert.Same(result, await behavior.Handle(command, () => Task.FromResult(result), default));

        await SeedProductAsync(db);

        var error = await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(
            () => behavior.Handle(command, () => Task.FromResult(result), default));

        Assert.Equal(SubscriptionQuotaKind.Products, error.Kind);
        // Singular, because a ceiling of one is a real case and "allows 1 products" reads as a bug.
        Assert.Contains("allows 1 product, and 1 has been used", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The renewal that reopens the allowance. This is the whole reason the term has a start date:
    /// without it a tenant would buy a second year and still be refused, which is the failure mode a
    /// quota feature cannot have.
    /// </summary>
    [Fact]
    public async Task Renewing_onto_a_new_term_reopens_the_allowance()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 2, productQuota: 0);
        await PostEntriesAsync(db, 2);

        await Assert.ThrowsAsync<SubscriptionQuotaExceededException>(() => ApproveAsync(db));

        var plan = await SeedPlanAsync(db);
        await new SetTenantSubscriptionCommandHandler(db, TimeProvider.System).Handle(
            new SetTenantSubscriptionCommand(OrganizationId, plan.Id, DateTimeOffset.UtcNow.AddDays(365)),
            CancellationToken.None);

        await ApproveAsync(db);
    }

    /// <summary>
    /// The renewal takes the plan's published ceilings when none are given, and the caller's when
    /// they are -- which is how an add-on is recorded (Rs 1,000 per additional 10,000 transactions).
    /// </summary>
    [Fact]
    public async Task A_renewal_defaults_to_the_plans_figures_and_accepts_add_on_overrides()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 0, productQuota: 0);
        var plan = await SeedPlanAsync(db);

        var handler = new SetTenantSubscriptionCommandHandler(db, TimeProvider.System);

        var plain = await handler.Handle(
            new SetTenantSubscriptionCommand(OrganizationId, plan.Id, DateTimeOffset.UtcNow.AddDays(365)),
            CancellationToken.None);

        Assert.Equal("Standard", plain.PlanName);
        Assert.Equal(20_000m, plain.SubscriptionAmount);
        Assert.Equal(50_000, plain.Usage.TransactionQuota);
        Assert.Equal(5_000, plain.Usage.ProductQuota);

        var withAddOns = await handler.Handle(
            new SetTenantSubscriptionCommand(
                OrganizationId, plan.Id, DateTimeOffset.UtcNow.AddDays(365),
                SubscriptionAmount: 23_000m, ProductQuota: 6_000, TransactionQuota: 80_000, IrdVerified: true),
            CancellationToken.None);

        Assert.Equal(23_000m, withAddOns.SubscriptionAmount);
        Assert.Equal(80_000, withAddOns.Usage.TransactionQuota);
        Assert.Equal(6_000, withAddOns.Usage.ProductQuota);
        Assert.True(withAddOns.IrdVerified);
    }

    /// <summary>A renewal with no plan records an unmetered term -- what extending a trial means --
    /// rather than silently inheriting the ceilings of a plan that was not chosen.</summary>
    [Fact]
    public async Task A_renewal_with_no_plan_is_an_unmetered_trial_term()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 5, productQuota: 5);

        var result = await new SetTenantSubscriptionCommandHandler(db, TimeProvider.System).Handle(
            new SetTenantSubscriptionCommand(OrganizationId, null, DateTimeOffset.UtcNow.AddDays(15)),
            CancellationToken.None);

        Assert.Null(result.PlanId);
        Assert.Equal("Trial", result.PlanName);
        Assert.Equal(0, result.Usage.TransactionQuota);
        Assert.Equal(0, result.Usage.ProductQuota);
    }

    [Fact]
    public async Task A_plan_id_that_names_nothing_is_a_404_rather_than_a_silent_trial()
    {
        var db = TestAppDbContext.Create();
        await SeedSubscriptionAsync(db, transactionQuota: 0, productQuota: 0);

        await Assert.ThrowsAsync<NotFoundException>(() => new SetTenantSubscriptionCommandHandler(db, TimeProvider.System).Handle(
            new SetTenantSubscriptionCommand(OrganizationId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(365)),
            CancellationToken.None));
    }

    /// <summary>
    /// The screen and the gate read the same code, so the bar a user sees and the refusal an Approve
    /// gets cannot disagree -- phase 26b's agree-by-construction, which phase 36 had to enforce the
    /// hard way when two ageing reports were patched into agreeing by coincidence instead.
    /// </summary>
    [Fact]
    public async Task The_displayed_usage_and_the_enforced_usage_are_the_same_number()
    {
        var db = TestAppDbContext.Create();
        var subscription = await SeedSubscriptionAsync(db, transactionQuota: 5, productQuota: 3);
        await PostEntriesAsync(db, 4);
        await SeedProductAsync(db);

        var usage = await SubscriptionUsageReader.ReadAsync(db, subscription, DateTimeOffset.UtcNow, default);

        Assert.Equal(4, usage.TransactionsUsed);
        Assert.Equal(1, usage.TransactionsRemaining);
        Assert.Equal(1, usage.ProductsUsed);
        Assert.Equal(2, usage.ProductsRemaining);
        Assert.True(usage.TransactionsMetered);

        // The fifth is allowed; the sixth would not be. Same reader, same numbers.
        await ApproveAsync(db);
    }

    [Fact]
    public void An_unmetered_usage_reports_no_remaining_rather_than_a_negative_one()
    {
        var usage = new SubscriptionUsage(0, 0, 0, 0, 0, 0, 0, 0);

        Assert.False(usage.TransactionsMetered);
        Assert.False(usage.ProductsMetered);
        Assert.Equal(0, usage.TransactionsRemaining);
        Assert.Equal(0, usage.ProductsRemaining);
    }

    /// <summary>Runs an Approve through the behavior and asserts it reached the handler. Throws
    /// <see cref="SubscriptionQuotaExceededException"/> when the behavior refuses, which is what the
    /// negative assertions below catch.</summary>
    private static async Task ApproveAsync(IAppDbContext db)
    {
        var behavior = new SubscriptionQuotaBehavior<ApproveInvoiceCommand, ApproveInvoiceResult>(db, TimeProvider.System);
        var expected = new ApproveInvoiceResult(Guid.NewGuid(), "INV-1", InvoiceStatus.Approved, DateTimeOffset.UtcNow);

        var actual = await behavior.Handle(
            new ApproveInvoiceCommand(OrganizationId, Guid.NewGuid()),
            () => Task.FromResult(expected),
            CancellationToken.None);

        Assert.Same(expected, actual);
    }

    private static async Task<TenantSubscription> SeedSubscriptionAsync(
        IAppDbContext db, int transactionQuota, int productQuota)
    {
        var subscription = TenantSubscription.CreateTrial(
            OrganizationId, default(AccountingFeatureSelections));

        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(CancellationToken.None);

        if (transactionQuota > 0 || productQuota > 0)
        {
            subscription.SetPlan(
                null, "Standard", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(364),
                20_000m, productQuota, transactionQuota, TenantSubscription.DefaultDailyAiScanQuota, 0,
                irdVerified: false);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return subscription;
    }

    private static async Task<SubscriptionPlan> SeedPlanAsync(IAppDbContext db)
    {
        var plan = SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", "Standard",
            "Best for SME organizations that require accounting & inventory tracking",
            2, 20_000m, 5_000, 50_000, TenantSubscription.DefaultDailyAiScanQuota,
            trackInventoryIncluded: true, multipleWarehousesIncluded: true, landedCostIncluded: true,
            manufacturingIncluded: false, posIncluded: false, multiCurrencyIncluded: true,
            developerApiIncluded: false);

        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(CancellationToken.None);

        return plan;
    }

    private static Task PostEntriesAsync(IAppDbContext db, int count)
    {
        return Task.WhenAll(Enumerable.Range(0, count)
            .Select(_ => PostEntryAsync(db, DocumentType.Invoice, Guid.NewGuid())));
    }

    private static async Task PostEntryAsync(
        IAppDbContext db,
        DocumentType documentType,
        Guid documentId,
        DateTimeOffset? postedAt = null,
        Guid? organizationId = null)
    {
        var entry = GlJournalEntry.Post(
            organizationId ?? OrganizationId,
            documentType,
            documentId,
            [new GlLineInput(Guid.NewGuid(), 100m, 0m), new GlLineInput(Guid.NewGuid(), 0m, 100m)]);

        db.GlJournalEntries.Add(entry);
        await db.SaveChangesAsync(CancellationToken.None);

        if (postedAt is { } stamp)
        {
            // PostedAt is stamped by the aggregate at post time, which is the correct behaviour and
            // the reason a back-dated entry can only be reached through the change tracker -- phase
            // 31's rule against weakening an invariant so a test can reach a state only time makes.
            var tracked = ((DbContext)db).Entry(entry);
            tracked.Property(nameof(GlJournalEntry.PostedAt)).CurrentValue = stamp;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task SeedProductAsync(IAppDbContext db)
    {
        var category = await new ErpApp.Application.Catalog.Commands.CreateProductCategory
            .CreateProductCategoryCommandHandler(db).Handle(
                new ErpApp.Application.Catalog.Commands.CreateProductCategory.CreateProductCategoryCommand(
                    OrganizationId, "General " + Guid.NewGuid(), null),
                CancellationToken.None);

        var unit = await new ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement
            .CreateUnitOfMeasurementCommandHandler(db).Handle(
                new ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement.CreateUnitOfMeasurementCommand(
                    OrganizationId, "Piece " + Guid.NewGuid(), "pc"),
                CancellationToken.None);

        await new ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductCommandHandler(
            db, new FakeDocumentNumberGenerator()).Handle(
                new ErpApp.Application.Catalog.Commands.CreateProduct.CreateProductCommand(
                    OrganizationId, ProductType.Service, "Consulting " + Guid.NewGuid(),
                    category.Id, unit.Id, null, true, 100m, 80m, VatRate.NoVat, 0, false),
                CancellationToken.None);
    }
}
