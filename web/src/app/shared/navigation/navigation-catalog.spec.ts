import { Route } from '@angular/router';

import { routes } from '../../app.routes';
import { EXCLUDED_PATHS, buildCatalog } from './navigation-catalog';
import { describe as describeUrl } from './history.service';

/**
 * Phase 33's client-side sweep guard, the counterpart to the server's `GlobalSearchSweepGuardTests`
 * and a sibling of phase-23's `sweep-guard.spec.ts`.
 *
 * The catalogue derives itself from the router, so it cannot go stale — but the *derivation* can be
 * wrong, and its two failure modes are both silent: a screen that yields no entry is simply
 * unreachable from the search and unpinnable to the tray, and an entry whose url matches no route
 * is a search result that navigates to a blank page. Neither breaks a build on its own.
 */
describe('NavigationCatalog', () => {
  const catalog = buildCatalog(routes);
  const allPaths = routes.map((r: Route) => r.path ?? '');

  it('covers every in-organization screen, or excludes it with a stated reason', () => {
    const listPaths = allPaths
      .filter((p) => p.startsWith('organizations/:id/'))
      .map((p) => p.slice('organizations/:id/'.length))
      .filter((p) => !p.includes(':'));

    const catalogued = new Set(catalog.map((x) => x.url));

    const missing = listPaths.filter((p) => !catalogued.has(`/${p}`) && !(p in EXCLUDED_PATHS));

    // A route here yields no catalogue entry, so it cannot be searched for or pinned to Quick
    // Links. Either the derivation missed it, or it belongs in EXCLUDED_PATHS with a reason.
    expect(missing).toEqual([]);
  });

  it('never offers a url that is not a real route', () => {
    const routeSet = new Set(allPaths);

    const dangling = catalog
      .map((x) => x.url.slice(1))
      // An Add entry addresses the detail route with the literal id `new`, so it is the *detail*
      // route that has to exist for it.
      .map((p) => (p.endsWith('/new') ? p.slice(0, -'/new'.length) : p))
      .filter((p) => {
        const isList = routeSet.has(`organizations/:id/${p}`);
        const hasDetail = [...routeSet].some((r) => r.startsWith(`organizations/:id/${p}/:`));
        return !isList && !hasDetail;
      });

    expect(dangling).toEqual([]);
  });

  it('gives every entry a name and an application-relative url', () => {
    // The server's validator refuses anything else, so an entry failing this could be pinned in the
    // UI and then rejected on save -- and an absolute url would be an open-redirect surface.
    const bad = catalog.filter(
      (x) =>
        x.name.trim().length === 0 ||
        x.area.trim().length === 0 ||
        !x.url.startsWith('/') ||
        x.url.startsWith('//') ||
        x.url.includes(':') ||
        x.url.includes('\\'),
    );

    expect(bad).toEqual([]);
  });

  it('offers an Add target exactly where a detail route exists', () => {
    const invoices = catalog.filter((x) => x.url.startsWith('/sales/invoices'));

    expect(invoices.map((x) => `${x.kind} ${x.url}`).sort()).toEqual([
      'Add /sales/invoices/new',
      'List /sales/invoices',
    ]);

    // A report has no detail route, so it gets one entry and no create form.
    const trialBalance = catalog.filter((x) => x.url.startsWith('/reports/trial-balance'));
    expect(trialBalance.length).toBe(1);
    expect(trialBalance[0].kind).toBe('List');
  });
});

describe('history describe()', () => {
  it('labels a record detail url with its list screen, never with the record', () => {
    const entry = describeUrl('/organizations/abc/sales/invoices/8f0f1f5c-0000-0000-0000-000000000001');

    // The reference product stores the record url but derives the label from the route, so its
    // popover shows "CRM · Contacts" for a contact detail page. This mirrors that exactly.
    expect(entry).toEqual({
      area: 'Sales',
      name: 'Invoices',
      url: '/organizations/abc/sales/invoices/8f0f1f5c-0000-0000-0000-000000000001',
    });
  });

  it('labels a screen under an area with its own name', () => {
    expect(describeUrl('/organizations/abc/reports/trial-balance')).toEqual({
      area: 'Reports',
      name: 'Trial Balance',
      url: '/organizations/abc/reports/trial-balance',
    });
  });

  it('ignores the screens the catalogue excludes, and anything outside an organization', () => {
    expect(describeUrl('/organizations/abc/home')).toBeNull();
    expect(describeUrl('/login')).toBeNull();
    expect(describeUrl('/organizations')).toBeNull();
  });

  it('strips the query string so one screen is one entry however it was filtered', () => {
    expect(describeUrl('/organizations/abc/products?page=2')?.url).toBe('/organizations/abc/products');
  });
});
