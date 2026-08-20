namespace ErpApp.Application.Common.Pagination;

/// <summary>
/// Uniform envelope for every paginated list/report query (roadmap Phase 16c, NFR-5.1) -- one
/// shared type used across all 30 retrofitted queries rather than a per-module duplicate, so the
/// Angular side has exactly one shape to unwrap. TotalPages/HasNextPage are deliberately not
/// modeled server-side -- they're pure derivations of Page/PageSize/TotalCount the client can
/// compute itself.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>
/// Shared paging bounds enforced by every retrofitted query's FluentValidation validator (see
/// PagingValidation.ValidatePaging) -- Page &lt; 1 or PageSize outside [1, MaxPageSize] is
/// rejected with a 400, never silently clamped (phase-16c-status.md's boundary-correctness exit
/// criterion). DefaultPageSize matches the pre-existing unpaginated screens' typical row count
/// closely enough that a caller which omits both params sees a page size that still feels
/// "give me everything" for a small tenant.
/// </summary>
public static class PagingDefaults
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}
