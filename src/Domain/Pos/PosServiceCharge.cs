namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 64 -- the one rule for whether a till line carries service charge, and at what rate.
///
/// <para>Phase 59 Decision G made it the location's rate times the product's flag. Phase 64's live read
/// (2026-10-02) found the third factor: <b>service charge is a dine-in charge</b>. On the vendor's
/// restaurant till the service-charge-applicable Chicken Momo (200, 10% service charge, 13% VAT) is a
/// 248.60 tile on Dine In and a 226.00 tile on Take Away and on Delivery -- 200 × 1.13, no service
/// charge. A charge for table service on a parcel nobody served is not one a restaurant may levy, and
/// since 2023-01-25 a mandatory one is not lawful at all (phase-61-status.md Decision A).</para>
///
/// <para>So a Take Away or Delivery line carries none; Dine In and the Retail till's own sale (no order
/// type, or <see cref="PosTab.Retail"/>) keep phase 61's rule, because a Retail location that switches
/// service charge on has chosen it. Both the sale engine and a restaurant order line ask here, so the
/// two cannot disagree about one product.</para>
/// </summary>
public static class PosServiceCharge
{
    public static decimal RateFor(PosLocationSettings settings, bool productApplicable, PosTab? orderType)
    {
        if (!settings.ServiceChargeEnabled || !productApplicable)
        {
            return 0m;
        }

        return orderType is PosTab.TakeAway or PosTab.Delivery ? 0m : settings.ServiceChargeRate;
    }
}
