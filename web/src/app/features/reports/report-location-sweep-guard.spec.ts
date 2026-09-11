import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Phase 35b -- the client half of `ReportLocationSweepGuardTests`.
 *
 * <p>The server guard proves every report query takes a Billing Location filter. That says nothing
 * about whether a screen ever sends one, and phase 32 is the worked example of the gap: the Sales
 * Master Report's query, DTO columns and endpoint carried `locationId` for three phases while its
 * Angular page sent nothing and rendered no LOCATION column. A filter with no control is a filter
 * that does not exist.</p>
 *
 * <p><b>Derived, not listed.</b> The page set comes from the router itself -- every
 * `organizations/:id/reports/...` route and the component directory it lazy-loads -- so a report
 * screen added later faces this question the moment it is routable rather than vanishing off the
 * bottom of a hand-kept list. That is the sweep-guard shape phases 27a, 32b, 34b and 35a
 * established, and reading the routes rather than the directory listing means a page nobody can
 * reach is not counted as a page that ships.</p>
 *
 * <p>Run from `web/` (`ng test`), because the paths resolve against `process.cwd()` -- the same
 * constraint `a11y-sweep-guard.spec.ts` carries and the phase-35a gotcha that names it.</p>
 */
const REPORTS_DIR = resolve(process.cwd(), 'src/app/features/reports');
const ROUTES = resolve(process.cwd(), 'src/app/app.routes.ts');

/**
 * Screens that deliberately carry no Billing Location control, each with the reason. The first six
 * mirror the server guard's census exemptions exactly (confirm-live 2026-09-10, all 49 report filter
 * bars read); the next three are this codebase's own, where the underlying row has no location at
 * all; the last is not a report.
 */
const EXEMPT: Readonly<Record<string, string>> = {
  'vat-summary-report-page': 'An IRD return, filed once per PAN. No control live.',
  'tds-report-page': 'An IRD return. No control live.',
  'annex-thirteen-report-page':
    'An IRD annex. No control live -- while Annex 5, the sales-side annex, does carry one and is '
    + 'therefore not exempt. Observed, not reconciled.',
  'ratio-analysis-page': 'Ratios over the whole organization. No control live.',
  'exceptional-report-page': 'A tenant-wide exception scan; its whole filter bar is a date range.',
  'user-log-page': 'Login events -- no document behind a row.',
  'migrated-sales-register-page':
    'A cutover import row is deliberately not a document (phase 21c) and carries no location.',
  'migrated-purchase-register-page': 'The Purchase Book counterpart, same reasoning.',
  'system-audit-report-page':
    'An Audit row records a command against a (DocumentType, DocumentId) pair and nothing else.',
  'report-index-page': 'The catalogue itself, not a report.',
};

function reportPages(): string[] {
  const routes = readFileSync(ROUTES, 'utf8');
  const directories = new Set<string>();

  for (const match of routes.matchAll(/import\('\.\/features\/reports\/([a-z0-9-]+)\//g)) {
    directories.add(match[1]);
  }

  return [...directories].sort();
}

function templateOf(page: string): string {
  return readFileSync(resolve(REPORTS_DIR, page, `${page}.html`), 'utf8');
}

describe('Report location sweep guard', () => {
  it('finds the report screens at all', () => {
    // Phase-34a's rule: a guard must assert its input is non-empty, not merely defined, or every
    // assertion below passes for the wrong reason.
    expect(reportPages().length).toBeGreaterThanOrEqual(50);
    expect(templateOf('trial-balance-page').length).toBeGreaterThan(0);
  });

  it('puts the Billing Location filter on every report screen that has the dimension', () => {
    const missing = reportPages()
      .filter((page) => !(page in EXEMPT))
      .filter((page) => !templateOf(page).includes('<app-report-location-filter'));

    expect(
      missing,
      '43 of the reference product\'s 49 report screens carry a Billing Location filter. These '
        + 'screens render none and give no reason -- either add <app-report-location-filter> or name '
        + 'the page in EXEMPT with the reason.',
    ).toEqual([]);
  });

  /** An exemption naming a screen that no longer exists is a reason nobody can check. */
  it('has no stale exemptions', () => {
    const pages = new Set(reportPages());

    expect(Object.keys(EXEMPT).filter((page) => !pages.has(page))).toEqual([]);
  });

  /**
   * The control's id has to be unique per page: two `<label for>` pointing at the same id is the
   * orphan-label defect phase 34a swept out of 152 places, reintroduced by a copy-paste.
   */
  it('gives every filter a page-specific control id', () => {
    const ids = reportPages()
      .filter((page) => !(page in EXEMPT))
      .map((page) => /\[controlId\]="'([^']+)'"/.exec(templateOf(page))?.[1]);

    expect(ids.filter((id) => id === undefined)).toEqual([]);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
