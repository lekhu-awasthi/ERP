namespace ErpApp.Application.Common.Exceptions;

/// <summary>
/// Phase 31 -- the credit-control twin of <see cref="StockAvailabilityWarningException"/>. Also a
/// 422, also confirmable, and deliberately a <b>separate type with its own override flag</b> rather
/// than a reuse of that one.
///
/// <para>The reference product shows two different dialogs -- "Negative Stock Balance" and "Crossed
/// Credit Limit" -- each with its own Dismiss/Continue, and an invoice can trip both. Folding them
/// onto one <c>OverrideWarning</c> flag would mean acknowledging the stock dialog silently
/// acknowledged a credit-limit breach the user was never shown. The two flags cost one bool and buy
/// the property that a Continue only ever waives the warning it was shown for.</para>
///
/// <para>The 422's ProblemDetails carries a <c>warningKind</c> extension ("StockAvailability" or
/// "CreditLimit") so the client knows which flag to set on the resubmit -- see
/// <c>ExceptionHandling</c>.</para>
/// </summary>
public sealed class CreditLimitWarningException(string message) : Exception(message);
