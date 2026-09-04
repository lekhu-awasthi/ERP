using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>
/// Resolves the account a realised exchange difference is booked to: the tenant's Forex Gain
/// account for a gain, its Forex Loss account for a loss (see TenantSettings' own doc comment for
/// why they are two accounts rather than one).
///
/// <para>Deliberately resolved <b>only when a difference actually exists</b>, never up front. Every
/// other account resolver in this codebase fails fast before any side effect, because the accounts
/// it needs are needed on every document of that type. These two are needed on almost none: a
/// single-currency tenant never produces a difference, and even a multi-currency one only produces
/// one when a settlement rate differs from a booking rate. Demanding them at Approve time
/// regardless would make every tenant configure two accounts to use a feature most of them never
/// touch -- the phase-20f "check that a flag-off tenant can still function" test, applied to
/// configuration rather than to an entitlement.</para>
/// </summary>
internal static class ForexAccountResolver
{
    /// <summary>
    /// Returns the account for <paramref name="difference"/>, whose sign follows the ledger
    /// convention used throughout this phase: <b>positive is a gain, negative is a loss.</b>
    /// Never called with zero -- callers skip the whole forex leg in that case, which is why the
    /// missing-account error can name the exact account the tenant needs.
    /// </summary>
    public static async Task<Guid> ResolveAsync(
        IAppDbContext db, Guid organizationId, decimal difference, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        return Resolve(settings, difference);
    }

    /// <summary>Overload for callers that have already loaded TenantSettings, so a single Approve
    /// never reads the row twice.</summary>
    public static Guid Resolve(TenantSettings settings, decimal difference)
    {
        if (difference == 0)
        {
            throw new InvalidOperationException("A forex account is only resolved for a non-zero exchange difference.");
        }

        if (difference > 0)
        {
            return settings.DefaultForexGainAccountId
                ?? throw new ConflictException(
                    "This transaction produces a foreign exchange gain, but no Default Forex Gain account is " +
                    "configured. Set it under Accounting Defaults before approving foreign-currency settlements.");
        }

        return settings.DefaultForexLossAccountId
            ?? throw new ConflictException(
                "This transaction produces a foreign exchange loss, but no Default Forex Loss account is " +
                "configured. Set it under Accounting Defaults before approving foreign-currency settlements.");
    }
}
