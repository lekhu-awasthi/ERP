/** Mirrors ErpApp.Application.Common.Pagination.PagedResult<T> -- the one wire shape every
 * retrofitted list/report endpoint returns (roadmap Phase 16c). TotalPages/hasNext are derived
 * client-side, matching the server's own "don't model what the client can compute" choice. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const DEFAULT_PAGE_SIZE = 50;

/** Matches ErpApp.Application.Common.Pagination.PagingDefaults.MaxPageSize -- used by dropdown/
 * picker call sites (e.g. populating a Customer/Product/Account <select>) that need "give me
 * everything" rather than a paginated slice. Not a full fix for a tenant with more rows than this
 * in a single picker (that needs a real searchable async select, a separate future concern) but
 * avoids silently truncating today's realistic org sizes to the 50-row list-page default. */
export const MAX_PAGE_SIZE = 200;
