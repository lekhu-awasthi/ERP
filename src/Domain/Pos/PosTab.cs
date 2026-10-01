using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 60 -- which tab the till opens on at a location (the vendor's Location Settings &gt;
/// General &gt; Default Tab). Which tabs exist depends on the location's <see cref="PosMode"/>:
/// Restaurant has Dine In, Take Away and Delivery; Retail has Retail and Delivery (the vendor's API
/// returns HeadOffice with <c>default_tab: "Retail"</c>). <see cref="For"/> is that rule, stated once.
/// </summary>
public enum PosTab
{
    Retail = 1,
    DineIn = 2,
    TakeAway = 3,
    Delivery = 4,
}

public static class PosTabs
{
    /// <summary>The tabs a till in <paramref name="mode"/> has, in the order it shows them; the
    /// first is what it opens on when no default is set or the set one no longer applies.</summary>
    public static IReadOnlyList<PosTab> For(PosMode mode) => mode switch
    {
        PosMode.Retail => [PosTab.Retail, PosTab.Delivery],
        PosMode.Restaurant => [PosTab.DineIn, PosTab.TakeAway, PosTab.Delivery],
        _ => [],
    };
}
