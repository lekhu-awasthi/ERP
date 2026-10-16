namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 93 -- what a document line does to stock, for the batch rule. Phase 51 had a <c>bool isReceipt</c>: a receipt
/// must name a batch and an issue may leave it blank, walking every batch oldest-first. The vendor refuses the blank
/// issue ("Batch is required for ...", read live 2026-10-15 on an invoice, a credit note and a debit note), and the
/// user chose its rule for the ERP's documents. The till keeps the walk: it has no batch picker yet (phase 91 § 5),
/// and a void or refund of an un-narrowed sale restocks per batch from the sale's own movements
/// (<c>IssuedStockRestock</c>). An enum rather than a second bool, so the compiler named every caller.
/// </summary>
public enum StockLineKind
{
    /// <summary>Creates stock (a Purchase Bill line, a Credit Note line): the batch is required, and minted when new.</summary>
    Receipt,

    /// <summary>Consumes stock on an ERP document (an Invoice line, a Debit Note line): the batch is required and must
    /// exist.</summary>
    Issue,

    /// <summary>Consumes stock at the till: the batch may be left blank, and the ledger walks every batch
    /// oldest-first.</summary>
    TillIssue,
}
