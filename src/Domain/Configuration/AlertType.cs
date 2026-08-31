namespace ErpApp.Domain.Configuration;

/// <summary>
/// What a scheduled alert reports on. Both members were live-confirmed as the complete option
/// list of the reference product's "Alert Type" dropdown (Phase 20e confirm-live; the module scan
/// had recorded the same two but had not established that the list was exhaustive).
///
/// Each member is a distinct data-sourcing job with its own query, resolved at dispatch time to
/// exactly one <see cref="ErpApp.Application"/>-layer content builder -- the same
/// one-strategy-per-enum-member registration shape IGlPostingRule&lt;T&gt; uses for posting rules.
/// </summary>
public enum AlertType
{
    DailyTransactionSummary,
    CrmReport,
}
