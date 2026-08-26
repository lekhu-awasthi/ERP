namespace ErpApp.Domain.Configuration;

/// <summary>
/// The two sections the reference product's Configurations &gt; Cost Terms screen splits into
/// (erp-module-scan.md Configurations §7). The category is what makes a given term selectable in
/// one consuming context and not the other -- landed-cost lines on a purchase versus expense
/// terms rolled into a BOM/Production Journal's cost -- so it's a real discriminator, not a
/// display-only grouping.
/// </summary>
public enum CostTermCategory
{
    /// <summary>Landed-cost items (Freight, Insurance, Customs Duty).</summary>
    AdditionalCost = 1,

    /// <summary>Expense Term values for BOM/Production Journal cost roll-up (Phase 25).</summary>
    ProductionCost = 2,
}
