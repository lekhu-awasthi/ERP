namespace ErpApp.Domain.Accounting;

/// <summary>The 5 root types every AccountGroup/Account ultimately rolls up to (architecture-spec.md §4.7).</summary>
public enum AccountRootType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}
