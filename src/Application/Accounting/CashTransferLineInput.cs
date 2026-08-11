namespace ErpApp.Application.Accounting;

/// <summary>Shared destination-line-input shape for Create/Update CashTransfer.</summary>
public sealed record CashTransferLineInput(Guid ToAccountId, decimal Amount);
