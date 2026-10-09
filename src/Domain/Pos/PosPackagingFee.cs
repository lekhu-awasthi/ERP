using ErpApp.Domain.Sales;

namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 70 -- the one rule for whether a till line carries a packaging fee, and on what terms: the
/// location's setting, times the product's flag, times <b>whether the food is a parcel</b> (the user's
/// answer of 2026-10-08: "every parcel").
///
/// <para>A parcel is a Take Away or Delivery sale, on either kind of till (the Retail till has a Delivery
/// tab), or dine-in food marked as take-away (phase 68). Plated dine-in food, a Retail-tab counter sale and
/// every ERP invoice carry none. It is the mirror of <see cref="PosServiceCharge"/>, which charges only
/// what is <i>not</i> a parcel (phase 64 Decision H) -- so a parcel from a dine-in tab may carry both, as
/// phase 68's <i>Service charge on take-away</i> allows.</para>
///
/// <para>The answer is frozen on the line it creates (phase 68's rule), so this is asked once: when an
/// order line is first sent, when food is marked as take-away, or when a till sale is rung up.</para>
/// </summary>
public static class PosPackagingFee
{
    public static PackagingFee For(PosLocationSettings settings, bool productApplicable, PosTab? orderType) =>
        orderType is PosTab.TakeAway or PosTab.Delivery ? ForParcel(settings, productApplicable) : PackagingFee.None;

    /// <summary>The terms a parcel takes, whatever tab it came from.</summary>
    public static PackagingFee ForParcel(PosLocationSettings settings, bool productApplicable) =>
        productApplicable ? settings.PackagingFee : PackagingFee.None;
}
