using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

/// <summary>
/// Phase 41 -- the plan catalogue aggregate and <c>TenantSubscription.SetPlan</c>, the mutator that
/// replaced phase 31's <c>Renew</c>. Phase 46 added the daily AI-scan ceiling and the purchased
/// location count to both.
/// </summary>
public class SubscriptionPlanAndTermTests
{
    private const int Scans = TenantSubscription.DefaultDailyAiScanQuota;

    private static SubscriptionPlan Standard() => SubscriptionPlan.Create(
        Guid.NewGuid(), "Standard", "Standard",
        "Best for SME organizations that require accounting & inventory tracking",
        2, 20_000m, 5_000, 50_000, Scans,
        trackInventoryIncluded: true, multipleWarehousesIncluded: true, landedCostIncluded: true,
        manufacturingIncluded: false, posIncluded: false, multiCurrencyIncluded: true,
        developerApiIncluded: false);

    [Fact]
    public void A_plan_carries_its_published_price_and_both_ceilings()
    {
        var plan = Standard();

        Assert.Equal("Standard", plan.Code);
        Assert.Equal(20_000m, plan.AnnualAmount);
        Assert.Equal(5_000, plan.ProductQuota);
        Assert.Equal(50_000, plan.TransactionQuota);
        Assert.True(plan.TrackInventoryIncluded);
        Assert.False(plan.ManufacturingIncluded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_plan_needs_a_code(string code)
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), code, "Standard", "d", 1, 1m, 1, 1, 1,
            false, false, false, false, false, false, false));
    }

    [Fact]
    public void A_plan_needs_a_name()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", " ", "d", 1, 1m, 1, 1, 1,
            false, false, false, false, false, false, false));
    }

    [Fact]
    public void A_plan_cannot_be_priced_negatively()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", "Standard", "d", 1, -1m, 1, 1, 1,
            false, false, false, false, false, false, false));
    }

    /// <summary>
    /// Zero is the <i>subscription's</i> "not metered" sentinel, never a plan's. A catalogue row with
    /// a zero ceiling would be a published tier that silently sells unlimited use -- the one mistake
    /// in this aggregate that costs money.
    /// </summary>
    [Theory]
    [InlineData(0, 50_000)]
    [InlineData(5_000, 0)]
    [InlineData(-1, 50_000)]
    [InlineData(5_000, -1)]
    public void A_plan_must_state_a_positive_ceiling_on_both_axes(int productQuota, int transactionQuota)
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", "Standard", "d", 1, 1m, productQuota, transactionQuota, Scans,
            false, false, false, false, false, false, false));
    }

    /// <summary>
    /// Phase 46 -- the scan ceiling joins the same rule for the same reason. A published tier stating
    /// no scan limit would be selling uncapped use of the one feature that costs money per call.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_plan_must_state_a_positive_daily_scan_ceiling(int dailyAiScanQuota)
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", "Standard", "d", 1, 1m, 5_000, 50_000, dailyAiScanQuota,
            false, false, false, false, false, false, false));
    }

    /// <summary>
    /// Phase 46 -- the published figure, identical on Basic, Standard and Professional and on all
    /// three term lengths (tiggapp.com/pricing, read 2026-09-15). Pinned so a later edit that varies
    /// it by tier has to change this test and say why.
    /// </summary>
    [Fact]
    public void A_plan_carries_the_published_daily_scan_ceiling()
    {
        Assert.Equal(20, Standard().DailyAiScanQuota);
        Assert.Equal(20, TenantSubscription.DefaultDailyAiScanQuota);
    }

    [Fact]
    public void A_new_trial_is_unmetered_on_both_axes_and_has_no_plan()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Null(subscription.PlanId);
        Assert.Equal(0m, subscription.SubscriptionAmount);
        Assert.Equal(0, subscription.ProductQuota);
        Assert.Equal(0, subscription.TransactionQuota);
        Assert.False(subscription.IrdVerified);
        Assert.Equal(subscription.OriginatedAt, subscription.TermStartsAt);
    }

    /// <summary>
    /// Phase 46, and the one place a trial is deliberately <b>not</b> unmetered. Phase 41's
    /// zero-means-no-limit is right for an allowance somebody bought; for scans it would mean a free
    /// trial with uncapped spend on a paid API, so the published 20 is seeded instead.
    /// </summary>
    [Fact]
    public void A_new_trial_is_metered_on_scans_even_though_it_is_metered_on_nothing_else()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Equal(20, subscription.DailyAiScanQuota);
        Assert.Equal(0, subscription.TransactionQuota);
        Assert.Equal(0, subscription.ProductQuota);
    }

    /// <summary>Phase 46 -- nobody has bought a location on a fresh trial, and the count is a record
    /// of purchases rather than of the seeded HeadOffice row.</summary>
    [Fact]
    public void A_new_trial_has_recorded_no_purchased_locations()
    {
        Assert.Equal(0, TenantSubscription.CreateTrial(Guid.NewGuid(), default).LocationQuota);
    }

    [Fact]
    public void SetPlan_records_the_whole_term_that_was_sold()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);
        var plan = Standard();
        var termStart = DateTimeOffset.UtcNow;

        subscription.SetPlan(
            plan.Id, plan.Name, termStart, termStart.AddDays(365),
            20_000m, plan.ProductQuota, plan.TransactionQuota, plan.DailyAiScanQuota,
            locationQuota: 3, irdVerified: true);

        Assert.Equal(plan.Id, subscription.PlanId);
        Assert.Equal("Standard", subscription.PlanName);
        Assert.Equal(termStart, subscription.TermStartsAt);
        Assert.Equal(20_000m, subscription.SubscriptionAmount);
        Assert.Equal(5_000, subscription.ProductQuota);
        Assert.Equal(50_000, subscription.TransactionQuota);
        Assert.Equal(20, subscription.DailyAiScanQuota);
        Assert.Equal(3, subscription.LocationQuota);
        Assert.True(subscription.IrdVerified);
    }

    /// <summary>
    /// Phase 31's rule, unchanged and now worth restating because a plan row carries its own
    /// <c>...Included</c> columns and the obvious next step is to copy them across. A billing event
    /// is not a re-negotiation of what the tenant may model: flipping TrackInventory off under a
    /// tenant with stock already in a FIFO ledger is the failure this prevents.
    /// </summary>
    [Fact]
    public void SetPlan_leaves_every_entitlement_flag_alone()
    {
        var subscription = TenantSubscription.CreateTrial(
            Guid.NewGuid(),
            new AccountingFeatureSelections(true, true, true, true, true, true, true));

        // Standard includes neither Manufacturing nor POS; the tenant has both.
        var plan = Standard();

        subscription.SetPlan(
            plan.Id, plan.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365),
            20_000m, plan.ProductQuota, plan.TransactionQuota, plan.DailyAiScanQuota,
            locationQuota: 0, irdVerified: false);

        Assert.True(subscription.IsEnabled(TenantFeature.Manufacturing));
        Assert.True(subscription.IsEnabled(TenantFeature.PosRetail));
        Assert.True(subscription.IsEnabled(TenantFeature.PosRestaurant));
    }

    /// <summary>A renewal is a new term, not a new tenant: the origin date never moves, which is what
    /// keeps "when did this customer join" answerable after the first renewal.</summary>
    [Fact]
    public void SetPlan_never_moves_the_tenants_origin_date()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);
        var origin = subscription.OriginatedAt;

        subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(30),
            0m, 0, 0, Scans, 0, irdVerified: false);

        Assert.Equal(origin, subscription.OriginatedAt);
        Assert.NotEqual(origin, subscription.TermStartsAt);
    }

    [Fact]
    public void SetPlan_needs_a_plan_name()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "  ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 0m, 0, 0, Scans, 0, false));
    }

    [Fact]
    public void SetPlan_refuses_a_term_that_ends_before_the_tenant_began()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(-10), subscription.OriginatedAt.AddDays(-1),
            0m, 0, 0, Scans, 0, false));
    }

    /// <summary>A term that ends before it begins would make the quota window empty, so every metered
    /// tenant would read as having used nothing forever.</summary>
    [Fact]
    public void SetPlan_refuses_a_term_that_ends_before_it_begins()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(40), DateTimeOffset.UtcNow.AddDays(30),
            0m, 0, 0, Scans, 0, false));
    }

    [Fact]
    public void SetPlan_refuses_a_negative_amount()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), -1m, 0, 0, Scans, 0, false));
    }

    /// <summary>Negative is neither a ceiling nor the sentinel, and would read as "unlimited" through
    /// every comparison downstream. Phase 46 added the scan axis to the same rule.</summary>
    [Theory]
    [InlineData(-1, 0, Scans)]
    [InlineData(0, -1, Scans)]
    [InlineData(0, 0, -1)]
    public void SetPlan_refuses_a_negative_quota(int productQuota, int transactionQuota, int dailyAiScanQuota)
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30),
            0m, productQuota, transactionQuota, dailyAiScanQuota, 0, false));
    }

    /// <summary>Phase 46 -- a purchased location count is a record, never a ceiling, but a negative
    /// one is not a record of anything either.</summary>
    [Fact]
    public void SetPlan_refuses_a_negative_purchased_location_count()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30),
            0m, 0, 0, Scans, locationQuota: -1, irdVerified: false));
    }

    /// <summary>Zero is explicitly allowed, and has to be: it is what extending a trial records.</summary>
    [Fact]
    public void SetPlan_accepts_zero_as_the_not_metered_sentinel()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 0m, 0, 0, Scans, 0, false);

        Assert.Equal(0, subscription.ProductQuota);
        Assert.Equal(0, subscription.TransactionQuota);
    }

    /// <summary>
    /// Phase 46 -- zero stays available on the scan axis too, so a tenant can be deliberately
    /// exempted. What differs from the other two is only the <i>default</i>: nothing reaches this
    /// state by accident, because neither <c>CreateTrial</c> nor the command's fallback produces it.
    /// </summary>
    [Fact]
    public void SetPlan_accepts_zero_scans_as_a_deliberate_exemption()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 0m, 0, 0, 0, 0, false);

        Assert.Equal(0, subscription.DailyAiScanQuota);
    }
}
