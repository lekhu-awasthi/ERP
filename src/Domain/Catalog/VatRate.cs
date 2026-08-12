namespace ErpApp.Domain.Catalog;

/// <summary>
/// Confirmed live: Tigg exposes exactly 3 fixed VAT options per product, not a tenant-editable
/// Configuration lookup (see erp-module-scan.md's Inventory > Products section) -- modeled as a
/// fixed enum rather than a lookup table. Always set explicitly by Product.Create/Update, so no
/// .HasDefaultValue is configured in ProductConfiguration and the enum-default EF gotcha
/// (CLAUDE.md's known gotchas) never applies.
/// </summary>
public enum VatRate
{
    NoVat,
    ZeroVat,
    ThirteenPercentVat,
}

/// <summary>Phase 5 addition -- the percent QuotationLine/InvoiceLine's VatAmount computation
/// multiplies against. NoVat (exempt) and ZeroVat (zero-rated) both compute to 0; only the fixed
/// 13% option contributes VAT.</summary>
public static class VatRateExtensions
{
    public static decimal ToPercent(this VatRate rate) => rate == VatRate.ThirteenPercentVat ? 0.13m : 0m;
}
