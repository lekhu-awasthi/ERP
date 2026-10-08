using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>Phase 60 -- the POS foundation's Domain invariants.</summary>
public class PosFoundationTests
{
    private static PosLocationSettings Saved(
        PosMode mode = PosMode.Restaurant,
        bool serviceChargeEnabled = true,
        decimal rate = 10m,
        IEnumerable<int>? denominations = null,
        PosTab? defaultTab = null)
    {
        var settings = PosLocationSettings.CreateDefault(Guid.NewGuid(), Guid.NewGuid());
        settings.Update(
            mode, serviceChargeEnabled, rate, Guid.NewGuid(), serviceChargeOnTakeAway: true, roundOffEnabled: false, Guid.NewGuid(),
            cashVerificationRequired: true, denominations ?? [1, 500, 100], defaultTab,
            printEstimateBill: true, printInvoice: true, printCreditNote: true, printKot: true,
            abbreviatedTaxInvoiceEnabled: false);
        return settings;
    }

    [Fact]
    public void Every_location_starts_with_no_till_and_any_location_can_be_given_one()
    {
        var headOffice = BillingLocation.CreateHeadOffice(Guid.NewGuid());
        Assert.Equal(PosMode.None, headOffice.PosMode);

        headOffice.SetPosMode(PosMode.Retail);
        Assert.Equal(PosMode.Retail, headOffice.PosMode);
    }

    [Fact]
    public void An_inactive_location_refuses_a_till_but_can_always_be_switched_off()
    {
        var branch = BillingLocation.Create(Guid.NewGuid(), "1002", "Branch", "Pokhara", null);
        branch.SetPosMode(PosMode.Restaurant);
        branch.Update("1002", "Branch", "Pokhara", null, isActive: false);

        Assert.Throws<InvalidOperationException>(() => branch.SetPosMode(PosMode.Retail));

        branch.SetPosMode(PosMode.None);
        Assert.Equal(PosMode.None, branch.PosMode);
    }

    [Fact]
    public void The_two_retired_location_types_are_gone_and_their_ordinals_unused()
    {
        Assert.Equal([BillingLocationType.HeadOffice, BillingLocationType.Standard], Enum.GetValues<BillingLocationType>());
        Assert.False(Enum.IsDefined(typeof(BillingLocationType), 3));
        Assert.False(Enum.IsDefined(typeof(BillingLocationType), 4));
    }

    [Fact]
    public void A_location_with_no_saved_settings_reads_as_the_vendors_defaults()
    {
        var settings = PosLocationSettings.CreateDefault(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal([1000, 500, 100, 50, 20, 10, 5, 2, 1], settings.Denominations);
        Assert.False(settings.ServiceChargeEnabled);
        Assert.Equal(0m, settings.ServiceChargeRate);
        Assert.False(settings.RoundOffEnabled);
        Assert.Null(settings.DefaultTab);
    }

    [Fact]
    public void Denominations_are_stored_largest_first_and_a_duplicate_or_non_positive_value_is_refused()
    {
        Assert.Equal([500, 100, 1], Saved(denominations: [1, 500, 100]).Denominations);

        Assert.Throws<InvalidOperationException>(() => Saved(denominations: []));
        Assert.Throws<InvalidOperationException>(() => Saved(denominations: [100, 100]));
        Assert.Throws<InvalidOperationException>(() => Saved(denominations: [100, 0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void An_enabled_service_charge_needs_a_rate_above_zero_and_at_most_a_hundred(decimal rate)
    {
        Assert.Throws<InvalidOperationException>(() => Saved(rate: rate));
    }

    [Fact]
    public void Switching_service_charge_off_clears_its_rate_and_account_together()
    {
        var settings = Saved(serviceChargeEnabled: false, rate: 10m);

        Assert.Equal(0m, settings.ServiceChargeRate);
        Assert.Null(settings.ServiceChargeAccountId);
    }

    [Fact]
    public void A_default_tab_must_be_one_of_the_modes_tabs_and_a_stale_one_falls_back_to_the_first()
    {
        Assert.Throws<InvalidOperationException>(() => Saved(PosMode.Restaurant, defaultTab: PosTab.Retail));
        Assert.Throws<InvalidOperationException>(() => Saved(PosMode.None, defaultTab: PosTab.Retail));

        var settings = Saved(PosMode.Restaurant, defaultTab: PosTab.TakeAway);
        Assert.Equal(PosTab.TakeAway, settings.EffectiveDefaultTab(PosMode.Restaurant));

        // The location is later switched to Retail: Take Away no longer exists there.
        Assert.Equal(PosTab.Retail, settings.EffectiveDefaultTab(PosMode.Retail));
        Assert.Null(settings.EffectiveDefaultTab(PosMode.None));
    }

    [Fact]
    public void The_walk_in_customer_is_flagged_and_cannot_be_deactivated()
    {
        var walkIn = Contact.CreateWalkInCustomer(Guid.NewGuid());

        Assert.True(walkIn.IsWalkInCustomer);
        Assert.Equal(ContactType.Customer, walkIn.Type);
        Assert.Throws<InvalidOperationException>(walkIn.Deactivate);
        Assert.True(walkIn.IsActive);

        var ordinary = Contact.Create(Guid.NewGuid(), ContactType.Customer, "Ram", "0001", null, null, null, null, null, 0m);
        Assert.False(ordinary.IsWalkInCustomer);
    }

    [Fact]
    public void A_payment_mode_defaults_to_Other_and_refuses_an_undefined_kind()
    {
        var mode = PaymentMode.Create(Guid.NewGuid(), "Bank Transfer");
        Assert.Equal(PaymentModeKind.Other, mode.Kind);
        Assert.Null(mode.AccountId);

        Assert.Throws<InvalidOperationException>(() => PaymentMode.Create(Guid.NewGuid(), "Bad", kind: 0));
    }
}
