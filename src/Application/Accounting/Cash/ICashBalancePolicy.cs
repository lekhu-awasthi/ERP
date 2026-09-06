namespace ErpApp.Application.Accounting.Cash;

/// <summary>
/// Phase 31 -- <c>TenantSettings.NegativeCashBalanceAction</c>, dead since phase 2, finally read.
///
/// <para><b>Scope: the three documents that can actually take money out of a Bank or Cash
/// account.</b> A Payment with Direction=Paid credits its own AccountId; a CashTransfer credits its
/// FromAccountId; a Journal Voucher can credit any account at all, including a bank. Everything
/// else in this codebase credits a payable, a receivable, VAT, inventory or income -- never a
/// Bank/Cash account -- so a policy hook on Invoice or Purchase Bill Approve would be a check that
/// can never fire. Quick Payment/Receipt are Payments and are covered by the same hook for free.</para>
///
/// <para><b>Why the caller passes outflows rather than this reading the document.</b> Three
/// different aggregates with three different shapes reach the same question ("which accounts does
/// this credit, and by how much"), and each of their handlers has already loaded the rows that
/// answer it. Passing a flat list keeps this from needing to know any of them -- the same reason
/// phase 25 gave <c>IStockAvailabilityPolicy</c> a document-agnostic
/// <c>CheckRequirementsAsync</c> beside its Invoice-shaped one.</para>
/// </summary>
public interface ICashBalancePolicy
{
    Task<CashBalanceCheckResult> CheckAsync(
        Guid organizationId, IReadOnlyCollection<CashOutflow> outflows, CancellationToken cancellationToken);
}

/// <summary>One account and the base-currency amount this document takes out of it.</summary>
public sealed record CashOutflow(Guid AccountId, decimal Amount);

/// <summary>
/// Names the first account that goes negative and by how much, so the message can say which one --
/// the reference product's dialog is per-contact for credit limits and per-item for stock, so
/// per-account is the consistent shape here. <see cref="AccountCode"/> is empty when
/// <see cref="Status"/> is Ok.
/// </summary>
public sealed record CashBalanceCheckResult(
    CashBalanceStatus Status, string AccountName, string AccountCode, decimal ProjectedBalance);
