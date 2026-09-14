namespace ErpApp.Application.Common.Exceptions;

/// <summary>Which ceiling was reached. Travels to the client as the <c>quotaKind</c> ProblemDetails
/// extension, so a screen can offer the right next step without parsing the message.</summary>
public enum SubscriptionQuotaKind
{
    Transactions = 1,
    Products = 2,
}

/// <summary>
/// Phase 41 -- the tenant has spent a metered allowance its plan sold it (transactions per term, or
/// products).
///
/// <para><b>A 409, not a 422.</b> Phase 31's two confirmable warnings are 422s because a user with
/// the right permission may knowingly continue past them; a quota is not confirmable by the person
/// hitting it. It sits with <c>ConflictException</c>'s subscription-expired message instead: the
/// organization's state, not this request, is what refuses it. What the user can do about it is buy
/// an add-on -- Rs 1,000 per additional 10,000 transactions or per additional 1,000 products -- which
/// happens off this product entirely (see docs/phase-41-status.md), so the message says so rather
/// than implying a button exists.</para>
///
/// <para>It carries its numbers so the message can be specific: a ceiling that refuses work without
/// saying what the ceiling is, or how much of it is spent, is indistinguishable from a bug.</para>
/// </summary>
public sealed class SubscriptionQuotaExceededException(
    SubscriptionQuotaKind kind, string planName, int used, int quota)
    : Exception(BuildMessage(kind, planName, used, quota))
{
    public SubscriptionQuotaKind Kind { get; } = kind;

    public int Used { get; } = used;

    public int Quota { get; } = quota;

    private static string BuildMessage(SubscriptionQuotaKind kind, string planName, int used, int quota)
    {
        var noun = kind == SubscriptionQuotaKind.Transactions ? "transaction" : "product";
        var unit = quota == 1 ? noun : noun + "s";
        var window = kind == SubscriptionQuotaKind.Transactions ? " for this subscription term" : string.Empty;
        var verb = used == 1 ? "has" : "have";

        return $"The {planName} plan allows {quota:N0} {unit}{window}, and {used:N0} {verb} been used. "
            + "Contact your Tigg representative to add capacity or move to a higher plan.";
    }
}
