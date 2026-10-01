namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 61 -- which front end raised an <see cref="Invoice"/>. Phase 59 Decision C: a till sale is
/// an ordinary Invoice, so the channel is a fact about where it came from and never a second
/// document type. The vendor's row carries the same field (<c>channel: "POS"</c>).
///
/// <para>Member 0 is <see cref="Erp"/> on purpose, and that is the one case where a zero member
/// meaning something is safe: every invoice that existed before this phase was raised from the ERP,
/// so the default is <i>true</i> of every backfilled row (phase 58's <c>InventoryTrackingMode</c>
/// gotcha is about a default that is false of them).</para>
/// </summary>
public enum SalesChannel
{
    Erp = 0,
    Pos = 1,
}
