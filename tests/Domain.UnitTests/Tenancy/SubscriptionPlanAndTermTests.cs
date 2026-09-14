using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

/// <summary>
/// Phase 41 -- the plan catalogue aggregate and <c>TenantSubscription.SetPlan</c>, the mutator that
/// replaced phase 31's <c>Renew</c>.
/// </summary>
public class SubscriptionPlanAndTermTests
{
    private static SubscriptionPlan Standard() => SubscriptionPlan.Create(
        Guid.NewGuid(), "Standard", "Standard",
        "Best for SME organizations that require accounting & inventory tracking",
        2, 20_000m, 5_000, 50_000,
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
            Guid.NewGuid(), code, "Standard", "d", 1, 1m, 1, 1,
            false, false, false, false, false, false, false));
    }

    [Fact]
    public void A_plan_needs_a_name()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", " ", "d", 1, 1m, 1, 1,
            false, false, false, false, false, false, false));
    }

    [Fact]
    public void A_plan_cannot_be_priced_negatively()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionPlan.Create(
            Guid.NewGuid(), "Standard", "Standard", "d", 1, -1m, 1, 1,
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
            Guid.NewGuid(), "Standard", "Standard", "d", 1, 1m, productQuota, transactionQuota,
            false, false, false, false, false, false, false));
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
        Assert.Equal(subscription.TrialStartsAt, subscription.TermStartsAt);
    }

    [Fact]
    public void SetPlan_records_the_whole_term_that_was_sold()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);
        var plan = Standard();
        var termStart = DateTimeOffset.UtcNow;

        subscription.SetPlan(
            plan.Id, plan.Name, termStart, termStart.AddDays(365),
            20_000m, plan.ProductQuota, plan.TransactionQuota, irdVerified: true);

        Assert.Equal(plan.Id, subscription.PlanId);
        Assert.Equal("Standard", subscription.PlanName);
        Assert.Equal(termStart, subscription.TermStartsAt);
        Assert.Equal(20_000m, subscription.SubscriptionAmount);
        Assert.Equal(5_000, subscription.ProductQuota);
        Assert.Equal(50_000, subscription.TransactionQuota);
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
            20_000m, plan.ProductQuota, plan.TransactionQuota, irdVerified: false);

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
        var origin = subscription.TrialStartsAt;

        subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(30),
            0m, 0, 0, irdVerified: false);

        Assert.Equal(origin, subscription.TrialStartsAt);
        Assert.NotEqual(origin, subscription.TermStartsAt);
    }

    [Fact]
    public void SetPlan_needs_a_plan_name()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "  ", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 0m, 0, 0, false));
    }

    [Fact]
    public void SetPlan_refuses_a_term_that_ends_before_the_tenant_began()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(-10), subscription.TrialStartsAt.AddDays(-1),
            0m, 0, 0, false));
    }

    /// <summary>A term that ends before it begins would make the quota window empty, so every metered
    /// tenant would read as having used nothing forever.</summary>
    [Fact]
    public void SetPlan_refuses_a_term_that_ends_before_it_begins()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow.AddDays(40), DateTimeOffset.UtcNow.AddDays(30),
            0m, 0, 0, false));
    }

    [Fact]
    public void SetPlan_refuses_a_negative_amount()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), -1m, 0, 0, false));
    }

    /// <summary>Negative is neither a ceiling nor the sentinel, and would read as "unlimited" through
    /// every comparison downstream.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void SetPlan_refuses_a_negative_quota(int productQuota, int transactionQuota)
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<InvalidOperationException>(() => subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30),
            0m, productQuota, transactionQuota, false));
    }

    /// <summary>Zero is explicitly allowed, and has to be: it is what extending a trial records.</summary>
    [Fact]
    public void SetPlan_accepts_zero_as_the_not_metered_sentinel()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        subscription.SetPlan(
            null, "Trial", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 0m, 0, 0, false);

        Assert.Equal(0, subscription.ProductQuota);
        Assert.Equal(0, subscription.TransactionQuota);
    }
}
