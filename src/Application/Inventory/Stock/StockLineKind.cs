namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 93 -- what a document line does to stock, for the batch rule. Phase 51 had a <c>bool isReceipt</c>: a receipt
/// must name a batch and an issue may leave it blank, walking every batch oldest-first. The vendor refuses the blank
/// issue ("Batch is required for ...", read live 2026-10-15 on an invoice, a credit note and a debit note), and the
/// user chose its rule for the ERP's documents. An enum rather than a second bool, so the compiler named every caller.
///
/// <para>Phase 94 -- the Retail till names its batch too (the vendor's till refuses a line without one, read live
/// 2026-10-20), so phase 93's third member, <c>TillIssue</c>, which let the till walk, is gone: a till sale is an
/// <see cref="Issue"/>. A restaurant bill still walks, because its lines come from an order line that carries no batch
/// (phase 94 Decision B); it never passes through this rule. A void or refund of anything that walked restocks per
/// batch from its own movements (<c>IssuedStockRestock</c>).</para>
/// </summary>
public enum StockLineKind
{
    /// <summary>Creates stock (a Purchase Bill line, a Credit Note line): the batch is required, and minted when new.</summary>
    Receipt,

    /// <summary>Consumes stock (an Invoice line, a Debit Note line, a Retail till sale's line): the batch is required
    /// and must exist.</summary>
    Issue,
}
