import { Route } from '@angular/router';

import { routes } from '../../app.routes';
import { APP_NAME, titleForUrl } from './page-title.strategy';

/**
 * Phase 34a — WCAG 2.4.2 Page Titled, proved over the whole route table rather than by spot check.
 *
 * The failure this guards is not a badly-worded title. It is a route that falls through the
 * derivation and renders as the bare app name, which is exactly the state all 141 routes were in
 * before this phase and is completely silent: the page renders, the tests pass, and only a screen
 * reader user notices that every navigation announces the same word.
 *
 * Built like `navigation-catalog.spec.ts`, whose route-config-driven shape it borrows: derive from
 * `routes`, so a screen added by a later phase is covered here with no edit.
 */
describe('PageTitleStrategy', () => {
  const paths = routes.map((r: Route) => r.path ?? '').filter((p) => !p.includes('**'));

  /** A concrete url for a route path, with `:params` filled in as a plausible guid. */
  function urlFor(path: string): string {
    return `/${path}`.replace(/:[A-Za-z]+/g, '3f2504e0-4f89-11d3-9a0c-0305e82c3301');
  }

  it('covers every route in the config (guards against an empty route table)', () => {
    expect(paths.length).toBeGreaterThan(120);
  });

  it('gives every route a title that says more than the app name', () => {
    const bare = paths
      .filter((p) => p !== '' && p !== 'organizations/:id')
      .filter((p) => titleForUrl(urlFor(p)) === APP_NAME);

    expect(bare, `these routes fall through to the bare app name:\n  ${bare.join('\n  ')}`).toEqual([]);
  });

  it('ends every title with the app name, so a tab is identifiable among many', () => {
    const wrong = paths.filter((p) => !titleForUrl(urlFor(p)).endsWith(APP_NAME));

    expect(wrong).toEqual([]);
  });

  it('distinguishes the screens from one another', () => {
    // Two screens sharing a title is 2.4.2's real failure mode, not a missing title: the user is
    // told something, and it is the same thing every time. Detail routes deliberately share their
    // list's title, so they are excluded -- everything else must be distinct.
    const listPaths = paths.filter((p) => !p.includes(':'));
    const titles = listPaths.map((p) => titleForUrl(urlFor(p)));
    const seen = new Map<string, string[]>();

    listPaths.forEach((p, i) => seen.set(titles[i], [...(seen.get(titles[i]) ?? []), p]));

    const collisions = [...seen.entries()].filter(([, ps]) => ps.length > 1);

    expect(
      collisions.map(([t, ps]) => `${t} <- ${ps.join(', ')}`),
      'two screens cannot share a page title',
    ).toEqual([]);
  });

  it('names the screen, its area and the app', () => {
    expect(titleForUrl('/organizations/abc/sales/invoices')).toBe(`Invoices · Sales · ${APP_NAME}`);
    expect(titleForUrl('/organizations/abc/accounting/accounts')).toBe(
      `Chart of Accounts · Accounting · ${APP_NAME}`,
    );
  });

  it('titles a create form as the singular of its list', () => {
    expect(titleForUrl('/organizations/abc/sales/invoices/new')).toBe(`New Invoice · Sales · ${APP_NAME}`);
    // "-ies" is the case a naive `slice(0, -1)` gets wrong, turning Quotations' sibling into
    // "New Bills Of Material" territory; pinned so the rule is not simplified back.
    expect(titleForUrl('/organizations/abc/inventory/bills-of-materials/new')).toContain('New Bills Of Material');
  });

  it('titles a record page as its screen, since the id segment carries no words', () => {
    expect(titleForUrl('/organizations/abc/sales/invoices/3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(
      `Invoices · Sales · ${APP_NAME}`,
    );
  });

  it('titles the screens outside any organization', () => {
    expect(titleForUrl('/login')).toBe(`Sign In · ${APP_NAME}`);
    expect(titleForUrl('/organizations')).toBe(`Your Organizations · ${APP_NAME}`);
  });
});
