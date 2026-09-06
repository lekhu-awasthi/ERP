namespace ErpApp.Application.Common.Exceptions;

/// <summary>
/// Phase 31 -- the third confirmable warning, for
/// <c>TenantSettings.NegativeCashBalanceAction</c> = Warn. 422 like its two siblings, told apart by
/// the <c>warningKind</c> extension ("NegativeCashBalance"); see
/// <see cref="CreditLimitWarningException"/> for why each warning gets its own type and its own
/// override flag rather than sharing one.
/// </summary>
public sealed class CashBalanceWarningException(string message) : Exception(message);
