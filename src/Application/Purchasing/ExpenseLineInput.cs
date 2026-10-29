using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Application.Purchasing;

/// <param name="CreditUse">Phase 99 -- what the expense was for (Rule 40); Taxable when omitted.</param>
/// <param name="CreditCategory">Phase 99 -- Rule 41's limit; Full when omitted. An expense names an account, never a
/// product, so there is no product default to fall back on.</param>
public sealed record ExpenseLineInput(
    Guid AccountId,
    decimal Amount,
    VatRate VatRate,
    VatCreditUse CreditUse = VatCreditUse.Taxable,
    VatCreditCategory CreditCategory = VatCreditCategory.Full);
