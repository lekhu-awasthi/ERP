namespace ErpApp.Domain.Catalog;

/// <summary>Confirmed live: every Product uses FIFO valuation (architecture-spec.md §3.5). A
/// single-value enum today, kept as an enum (not hardcoded) since the FIFO costing engine
/// (Phase 7) is expected to branch on it.</summary>
public enum ValuationMethod
{
    Fifo,
}
