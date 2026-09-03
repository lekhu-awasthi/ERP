using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 26a -- the Account / Parent / Group Type / Account Class quartet that three of this
/// phase's reports show on every row (General Ledger Summary and GL Master Report show all four;
/// Detail General Ledger heads each section with the first). Read live on 2026-09-02:
/// <list type="bullet">
/// <item><b>Account</b> renders as "Name (Code)" throughout the reference product.</item>
/// <item><b>Parent</b> is the account's own immediate AccountGroup.</item>
/// <item><b>Group Type</b> is the <i>top-level</i> group that group descends from ("Current
/// Assets", "Indirect Expenses", ...), not the immediate one -- confirmed by rows where the two
/// differ, e.g. Parent "Cash and Bank Balance" under Group Type "Current Assets".</item>
/// <item><b>Account Class</b> is the root type (Assets / Liability / Income / Expenses).</item>
/// </list>
///
/// <para>The ancestor walk is the mirror image of <c>ITreeQuery&lt;AccountGroup&gt;</c>'s
/// descendant walk (Phase 8a), and like it, it runs in memory over the tenant's whole group list
/// rather than as a recursive CTE: a chart of accounts is small, the whole set is needed anyway to
/// name every Parent, and one round trip beats one per row. The walk is depth-capped so a cyclic
/// ParentGroupId -- which no code path creates, but no database constraint forbids -- degrades to
/// "the deepest group we reached" instead of hanging a report thread.</para>
/// </summary>
public sealed class GlAccountClassification
{
    /// <summary>Deeper than any real chart of accounts; the cap exists only so a cycle terminates.</summary>
    private const int MaxGroupDepth = 64;

    private readonly Dictionary<Guid, AccountFacts> _byAccountId;

    private GlAccountClassification(Dictionary<Guid, AccountFacts> byAccountId) => _byAccountId = byAccountId;

    public IReadOnlyCollection<AccountFacts> Accounts => _byAccountId.Values;

    public AccountFacts? For(Guid accountId) => _byAccountId.GetValueOrDefault(accountId);

    /// <summary>
    /// Loads every account in the organization, active or not. Inactive accounts are deliberately
    /// included: a GL line posted against an account that was later deactivated still exists, and a
    /// ledger that silently dropped it would not balance. (Trial Balance's active-only filter is a
    /// different question -- it lists the chart of accounts, not the postings.)
    /// </summary>
    public static async Task<GlAccountClassification> LoadAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var groups = await db.AccountGroups
            .Where(g => g.OrganizationId == organizationId)
            .Select(g => new { g.Id, g.Name, g.ParentGroupId })
            .ToListAsync(cancellationToken);

        var groupsById = groups.ToDictionary(g => g.Id);

        string TopLevelGroupName(Guid groupId)
        {
            var currentId = groupId;
            for (var depth = 0; depth < MaxGroupDepth; depth++)
            {
                if (!groupsById.TryGetValue(currentId, out var group))
                {
                    return string.Empty;
                }

                if (group.ParentGroupId is not { } parentId)
                {
                    return group.Name;
                }

                currentId = parentId;
            }

            return groupsById.TryGetValue(currentId, out var deepest) ? deepest.Name : string.Empty;
        }

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == organizationId)
            .Select(a => new { a.Id, a.Code, a.Name, a.RootType, a.GroupId })
            .ToListAsync(cancellationToken);

        var byAccountId = accounts.ToDictionary(
            a => a.Id,
            a => new AccountFacts(
                a.Id,
                a.Code,
                a.Name,
                a.GroupId,
                groupsById.TryGetValue(a.GroupId, out var group) ? group.Name : string.Empty,
                TopLevelGroupName(a.GroupId),
                a.RootType));

        return new GlAccountClassification(byAccountId);
    }

    /// <param name="GroupId">The account's own immediate group id -- what a Group filter matches
    /// on, since group <i>names</i> are not unique across a chart of accounts.</param>
    /// <param name="ParentGroupName">The account's own immediate group.</param>
    /// <param name="GroupTypeName">The top-level group that group descends from.</param>
    public sealed record AccountFacts(
        Guid AccountId,
        string AccountCode,
        string AccountName,
        Guid GroupId,
        string ParentGroupName,
        string GroupTypeName,
        AccountRootType RootType);
}
