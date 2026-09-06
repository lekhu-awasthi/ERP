namespace ErpApp.Application.Sales.Credit;

/// <summary>
/// Phase 31 -- the credit-control seam, deliberately shaped as a near-copy of
/// <c>IStockAvailabilityPolicy</c> rather than as a pipeline behavior.
///
/// <para><b>Why not a marker interface on a fifth pipeline behavior.</b> <c>LockDateBehavior</c> and
/// <c>FeatureGateBehavior</c> can ride the pipeline because everything they need is on the request:
/// a document type, an id, a flag name. This check needs the document's <i>lines</i> (to get its
/// total), the contact's whole approved ledger, and the tenant's setting, and it has to run after
/// the handler has loaded the document but before it assigns a number and posts. That is
/// phase-20f's lesson stated again: a conditional gate whose condition is data the handler is
/// already loading belongs in the handler. It is also, concretely, the shape phase 7 chose for the
/// identical problem, and having the two members of the same setting family behave differently
/// would be worse than either choice on its own.</para>
///
/// <para>One implementation, one caller (Invoice Approve). It is an interface anyway because that is
/// what lets a test drive Reject/Warn/Ok without seeding a whole ledger, and because
/// <c>IStockAvailabilityPolicy</c> earned the same shape and grew a second caller two phases
/// later.</para>
/// </summary>
public interface ICreditLimitPolicy
{
    Task<CreditLimitCheckResult> CheckAsync(
        Guid organizationId, Guid contactId, decimal documentAmount, CancellationToken cancellationToken);
}

/// <summary>
/// What the live "Crossed Credit Limit" dialog needs to render: which contact, and the projected
/// balance. The reference product shows <c>Adhitya Bhandari (Rd0005)   Nrs.26,800</c> -- and that
/// 26,800 is the contact's closing balance <i>plus this document</i>, not the amount by which the
/// limit is exceeded (confirmed live 2026-09-06 against a limit of 100 and a prior balance of
/// 21,800). <see cref="CreditLimit"/> rides along so the message can name both numbers, which is
/// strictly more useful than the live dialog and costs nothing.
/// </summary>
public sealed record CreditLimitCheckResult(
    CreditLimitStatus Status,
    string ContactName,
    string ContactCode,
    decimal ProjectedBalance,
    decimal CreditLimit);
