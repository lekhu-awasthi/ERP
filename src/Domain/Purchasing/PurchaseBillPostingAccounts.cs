namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Phase 87 -- the accounts a purchase bill's approval posts to, handed to <see cref="PurchaseBill.RecordPostingAccounts"/>
/// so the bill keeps them. <see cref="LineDebitAccountIds"/> is one per line, in line order. A null is a leg the bill does
/// not post (no Goods line, no Additional Cost).
/// </summary>
public sealed record PurchaseBillPostingAccounts(
    Guid PayableAccountId,
    Guid? VatReceivableAccountId,
    Guid? TdsPayableAccountId,
    Guid? InventoryAccountId,
    Guid? LandedCostClearingAccountId,
    IReadOnlyList<Guid> LineDebitAccountIds,
    Guid? InventoryAdjustmentAccountId = null);
