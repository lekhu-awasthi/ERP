namespace ErpApp.Application.Common.Trees;

/// <summary>
/// "Get full subtree" reads for an adjacency-list tree entity (architecture-spec.md §5's
/// AccountGroup/ContactGroup/ProductCategory family) -- first needed by Phase 8a's Balance Sheet,
/// which rolls a top-level AccountGroup's balance up across every descendant subgroup's Accounts.
///
/// Implemented as an in-memory BFS over the tenant's full row set (Application.Accounting's
/// AccountGroupTreeQuery), not the architecture spec's originally-recommended raw SQL Server
/// recursive CTE -- see that class's doc comment for why. GetSubtreeIdsAsync's signature is
/// deliberately provider-agnostic so a real CTE-backed implementation could replace it later
/// without touching call sites, if a tenant's tree ever grows past "a few levels" (architecture-
/// spec.md §5's own sizing assumption for this data).
/// </summary>
public interface ITreeQuery<TEntity>
{
    /// <summary>Returns rootId plus every id reachable from it by following ParentId downward
    /// (i.e. the root's own id and all of its descendants' ids, not its ancestors).</summary>
    Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(Guid organizationId, Guid rootId, CancellationToken cancellationToken);
}
