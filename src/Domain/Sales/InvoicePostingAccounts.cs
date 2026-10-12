namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 87 -- the accounts an invoice's approval posts to, handed to <see cref="Invoice.RecordPostingAccounts"/> so the
/// sale keeps them. <see cref="LineSalesAccountIds"/> is one per line, in line order. A null is a leg the sale does not
/// post (no VAT, no Goods line, no service charge or packaging fee).
/// </summary>
public sealed record InvoicePostingAccounts(
    Guid ReceivableAccountId,
    Guid? VatPayableAccountId,
    Guid? CogsAccountId,
    Guid? InventoryAccountId,
    Guid? ServiceChargeAccountId,
    Guid? PackagingFeeAccountId,
    IReadOnlyList<Guid> LineSalesAccountIds);
