import cases from './search-cases.json';
import { matchesLookup } from './lookup-filter';

/**
 * Phase 47 (phase 40 carried item #5) — the client half of `search-cases.json`.
 *
 * Phase 40 shipped two implementations of "does this row match what I typed" and said so plainly:
 * the paginated lists send a term to the server, the Configurations lookups filter in the browser,
 * *they agree today and nothing enforces that they keep agreeing*. This file and
 * `SearchParityTests` (Api.IntegrationTests, against a real SQL Server — the case-insensitivity is
 * the collation's, and the InMemory provider does not have it) are what enforce it. Neither is the
 * source of truth for the other; the JSON is.
 */
describe('the lookup filter agrees with the shared search table', () => {
  const rows = (cases as { cases: { why: string; haystack: string; needle: string; matches: boolean }[] }).cases;

  it('reads a table with cases in it at all', () => {
    // Without this, a table that failed to resolve would make every case below pass vacuously —
    // the way the guard in phase 34a passed over an empty stylesheet.
    expect(rows.length).toBeGreaterThanOrEqual(20);
  });

  for (const row of rows) {
    it(`${row.why}: '${row.needle}' against '${row.haystack}' is ${row.matches}`, () => {
      expect(matchesLookup(row.needle, row.haystack)).toBe(row.matches);
    });
  }

  it('matches when any one of several fields matches, which is what the multi-field callers rely on', () => {
    // Not in the shared table, because it is not a question about the *term* — the server's
    // handlers OR their own columns in their own Where, so there is nothing to agree with. It is
    // here because Alerts passes two fields and Reporting Tags passes a nested one.
    expect(matchesLookup('recipient', 'Low stock', 'ops@example.com, recipient@example.com')).toBe(true);
    expect(matchesLookup('recipient', 'Low stock', null)).toBe(false);
  });
});
