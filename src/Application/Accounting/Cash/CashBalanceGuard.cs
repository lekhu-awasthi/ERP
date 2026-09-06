using ErpApp.Application.Common.Exceptions;

namespace ErpApp.Application.Accounting.Cash;

/// <summary>
/// The Reject/Warn/Ok branch, extracted because three approve handlers make the identical decision
/// and the wording of a hard block versus a confirmable warning is exactly the kind of thing that
/// drifts once it is copied. The document noun is passed in so each message reads naturally
/// ("Approving this payment would..."), which is the only part that legitimately differs.
/// </summary>
internal static class CashBalanceGuard
{
    internal static void Enforce(CashBalanceCheckResult result, bool overrideWarning, string documentNoun)
    {
        if (result.Status == CashBalanceStatus.Reject)
        {
            throw new ConflictException(
                $"Approving this {documentNoun} would take {result.AccountName} ({result.AccountCode}) to " +
                $"{result.ProjectedBalance:0.##}, a negative cash and bank balance. " +
                "Fund the account or change the Negative Cash Balance setting before approving.");
        }

        if (result.Status == CashBalanceStatus.Warn && !overrideWarning)
        {
            throw new CashBalanceWarningException(
                $"This {documentNoun} takes {result.AccountName} ({result.AccountCode}) to " +
                $"{result.ProjectedBalance:0.##}, a negative cash and bank balance. " +
                "Approve again to continue anyway.");
        }
    }
}
