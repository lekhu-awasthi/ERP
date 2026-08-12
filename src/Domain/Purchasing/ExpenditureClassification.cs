namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Annex 13 Capital-vs-Others purchase-expenditure classification (architecture-spec.md §4.5's
/// explicit recommendation) -- the scan never found this field's real UI location (still an open
/// item per erp-module-scan.md's closing notes), so this is added speculatively rather than
/// reverse-engineered. Shared by PurchaseBillLine and ExpenseLine. Defaults to Others.
/// </summary>
public enum ExpenditureClassification
{
    Others,
    Capital,
}
