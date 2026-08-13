using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting;

/// <summary>
/// ITreeQuery&lt;AccountGroup&gt; via an in-memory BFS over the tenant's full AccountGroup set,
/// not architecture-spec.md §5's originally-recommended raw SQL Server recursive CTE. Deliberate
/// scope decision (Phase 8a): that section's own reasoning for AccountGroup using an adjacency
/// list over HIERARCHYID in the first place -- "the observed depth (a few levels) doesn't need
/// [it]" -- applies just as well here, and a portable IAppDbContext LINQ query (works identically
/// against the InMemory provider in unit tests and real SQL Server in production) avoids yet
/// another instance of the Database.SqlQuery&lt;T&gt; composability gotchas this codebase has hit
/// repeatedly (see CLAUDE.md's "Known gotchas"). If a tenant's Chart of Accounts ever grows large
/// enough for this to matter, swap this class for a real CTE-backed one behind the same interface.
/// </summary>
public sealed class AccountGroupTreeQuery(IAppDbContext db) : ITreeQuery<AccountGroup>
{
    public async Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(
        Guid organizationId, Guid rootId, CancellationToken cancellationToken)
    {
        var groups = await db.AccountGroups
            .Where(g => g.OrganizationId == organizationId)
            .Select(g => new { g.Id, g.ParentGroupId })
            .ToListAsync(cancellationToken);

        var childrenByParent = groups
            .Where(g => g.ParentGroupId is not null)
            .ToLookup(g => g.ParentGroupId!.Value, g => g.Id);

        var subtreeIds = new List<Guid> { rootId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rootId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var childId in childrenByParent[current])
            {
                subtreeIds.Add(childId);
                frontier.Enqueue(childId);
            }
        }

        return subtreeIds;
    }
}
