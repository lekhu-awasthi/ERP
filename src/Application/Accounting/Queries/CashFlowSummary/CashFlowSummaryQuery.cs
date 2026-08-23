using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.CashFlowSummary;

/// <summary>
/// Phase 19 decision #2 -- a direct-method summary of actual Bank/Cash account movements
/// (Account.Kind == Bank || Cash, Phase 17), NOT an indirect-method statement. Live-confirmed
/// against the real Tigg screen: its only filters are Period + a Bank Accounts picker + Compare, no
/// Operating/Investing/Financing classification anywhere, and this codebase has no such
/// classification field on Account/AccountGroup. BankAccountId narrows to one Account; null means
/// every Bank/Cash account (the live screen's "All").
/// </summary>
public sealed record CashFlowSummaryQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate, Guid? BankAccountId)
    : IRequest<CashFlowSummaryDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CashFlowSummaryView;
}

/// <summary>
/// Mirrors the live screen's 6 rows (Starting Balance / Received From Customer / Other Receipts /
/// Paid To Supplier / Other Payments / Ending Balance). "Received From Customer"/"Paid To Supplier"
/// each include every GL line belonging to a Payment whose Direction+Contact.Type match (both
/// debit and credit sides, so a voided payment's mirror-image reversal still lands in the same
/// bucket, just the opposite column -- see phase-19-status.md decision #2). Every other Bank/Cash
/// line splits by its own Debit/Credit into Other Receipts (Debit) or Other Payments (Credit).
/// </summary>
public sealed record CashFlowSummaryDto(
    DateOnly FromDate,
    DateOnly ToDate,
    decimal StartingBalance,
    decimal ReceivedFromCustomerCashIn,
    decimal ReceivedFromCustomerCashOut,
    decimal OtherReceiptsCashIn,
    decimal OtherReceiptsCashOut,
    decimal PaidToSupplierCashIn,
    decimal PaidToSupplierCashOut,
    decimal OtherPaymentsCashIn,
    decimal OtherPaymentsCashOut,
    decimal EndingBalance)
{
    public decimal ReceivedFromCustomerBalance => ReceivedFromCustomerCashIn - ReceivedFromCustomerCashOut;
    public decimal OtherReceiptsBalance => OtherReceiptsCashIn - OtherReceiptsCashOut;
    public decimal PaidToSupplierBalance => PaidToSupplierCashIn - PaidToSupplierCashOut;
    public decimal OtherPaymentsBalance => OtherPaymentsCashIn - OtherPaymentsCashOut;
}
