namespace ErpApp.Application.Payments;

/// <summary>Phase 17 (docs/phase-17-status.md decision #6) -- supplied on Create/UpdatePayment
/// only when the chosen PaymentMode has RequiresChequeDetails == true; the handler creates/updates
/// a linked Payments.Cheque row from this.</summary>
public sealed record ChequeDetailsInput(string ChequeNo, DateOnly ChequeDate, DateOnly? ReceivedDate);
