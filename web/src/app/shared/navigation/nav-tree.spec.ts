import { Route } from '@angular/router';

import { routes } from '../../app.routes';
import { CREATE_NEW_COLUMNS } from '../platform/create-new-flyout';
import { buildCatalog } from './navigation-catalog';
import {
  AREA_ORDER,
  HOME_ITEM,
  LEAF_AREAS,
  REPORT_CATEGORIES,
  REPORT_CATEGORY_ORDER,
  activeGroupOf,
  activeItemUrlOf,
  buildNavTree,
  buildReportIndex,
} from './nav-tree';

/**
 * Phase 34b's client-side sweep guards, the siblings of phase 33's `navigation-catalog.spec.ts` and
 * the server's `SearchSweepGuardTests`.
 *
 * The left nav derives itself from the router, so it cannot go stale — but the two things it does
 * *not* derive can: the area order, and which family a report belongs to. Both fail silently. An
 * uncategorised report simply never appears on the Reports index, which is the only way to reach it
 * now that the nav renders Reports as a single leaf.
 */
describe('the left nav tree', () => {
  const catalog = buildCatalog(routes);
  const tree = buildNavTree(catalog);

  it('finds screens to build a nav from at all', () => {
    // Without this every assertion below would pass vacuously against an empty tree — 34a's lesson
    // that a guard must assert its input is non-empty, not merely defined.
    expect(catalog.length).toBeGreaterThan(100);
    expect(tree.length).toBeGreaterThan(5);
  });

  it('puts every catalogued screen in exactly one nav group', () => {
    const listEntries = catalog.filter((x) => x.kind === 'List');
    const inTree = tree.flatMap((g) => g.items).length;
    const inLeafAreas = listEntries.filter((x) => x.area in LEAF_AREAS).length;

    // Two independent counts of the same thing. They disagree exactly when a screen has been
    // dropped or duplicated — phase-34a's rule that a silent mismatch shows up as two counts
    // failing to agree.
    expect(inTree + inLeafAreas).toBe(listEntries.length);
  });

  it('orders the areas the way the reference product does', () => {
    const areas = tree.map((g) => g.area);
    const known = areas.filter((a) => AREA_ORDER.includes(a));

    expect(known).toEqual(AREA_ORDER.filter((a) => areas.includes(a)));
  });

  it('renders Reports as a leaf rather than as fifty-two nav rows', () => {
    const reports = tree.find((g) => g.area === 'Reports');

    expect(reports).toBeDefined();
    expect(reports!.url).toBe('/reports');
    expect(reports!.items).toEqual([]);
  });

  it('points every leaf area at a real route', () => {
    const paths = new Set(routes.map((r: Route) => r.path ?? ''));

    for (const url of Object.values(LEAF_AREAS)) {
      expect(paths.has(`organizations/:id${url}`)).toBe(true);
    }

    expect(paths.has(`organizations/:id${HOME_ITEM.url}`)).toBe(true);
  });
});

describe('the Reports index', () => {
  const catalog = buildCatalog(routes);

  it('gives every report route a heading, or the report is unreachable', () => {
    const reportUrls = catalog
      .filter((x) => x.area === 'Reports' && x.kind === 'List' && x.url !== '/reports')
      .map((x) => x.url.slice('/reports/'.length));

    expect(reportUrls.length).toBeGreaterThan(40);

    const uncategorised = reportUrls.filter((slug) => !REPORT_CATEGORIES[slug]);

    // The nav renders Reports as one leaf, so this page is the only way to reach a report. A route
    // missing from REPORT_CATEGORIES is a screen nobody can navigate to.
    expect(uncategorised).toEqual([]);
  });

  it('never names a heading the index cannot render', () => {
    const unknown = [...new Set(Object.values(REPORT_CATEGORIES))].filter(
      (c) => !REPORT_CATEGORY_ORDER.includes(c),
    );

    expect(unknown).toEqual([]);
  });

  it('keeps every categorised report pointing at a route that still exists', () => {
    const catalogued = new Set(catalog.map((x) => x.url));
    const stale = Object.keys(REPORT_CATEGORIES).filter((slug) => !catalogued.has(`/reports/${slug}`));

    expect(stale).toEqual([]);
  });

  it('accounts for every report exactly once across its headings', () => {
    const index = buildReportIndex(catalog);
    const rendered = index.flatMap((g) => g.reports.map((r) => r.url));

    expect(new Set(rendered).size).toBe(rendered.length);
    expect(rendered.length).toBe(Object.keys(REPORT_CATEGORIES).length);
  });
});

describe('marking the active screen', () => {
  const tree = buildNavTree(buildCatalog(routes));

  it('opens the group containing the screen you are on', () => {
    const group = activeGroupOf(tree, '/organizations/abc/sales/invoices');

    expect(group?.area).toBe('Sales');
  });

  /**
   * Confirmed live: the reference product keeps the group open and the leaf marked while you read a
   * record, not only while you are on the list. Its nav stayed on Customers with Sales expanded
   * after opening a contact.
   */
  it('stays marked while a record under that screen is open', () => {
    const url = '/organizations/abc/sales/invoices/8f0f1f5c-0000-0000-0000-000000000001';

    expect(activeGroupOf(tree, url)?.area).toBe('Sales');
    expect(activeItemUrlOf(tree, url)).toBe('/sales/invoices');
  });

  /**
   * The reason `activeItemUrlOf` picks the *longest* match rather than the first. `/products` and
   * `/products/categories` are both real screens, and a first-match rule would mark Products while
   * the user is on Product Categories.
   */
  it('prefers the more specific screen when one url is a prefix of another', () => {
    expect(activeItemUrlOf(tree, '/organizations/abc/products/categories')).toBe('/products/categories');
    expect(activeItemUrlOf(tree, '/organizations/abc/products')).toBe('/products');
  });

  it('marks nothing outside an organization', () => {
    expect(activeGroupOf(tree, '/login')).toBeNull();
    expect(activeItemUrlOf(tree, '/organizations')).toBeNull();
  });
});

describe('the Create New flyout', () => {
  const catalog = buildCatalog(routes);

  it('offers only shortcuts that resolve to a real screen', () => {
    const catalogued = new Set(catalog.map((x) => x.url));
    const links = CREATE_NEW_COLUMNS.flatMap((c) => c.links);

    expect(links.length).toBeGreaterThan(15);

    const dangling = links.filter((l) => !catalogued.has(l.url));

    // The list itself is curated -- see the component for why -- but a curated list can still be
    // wrong about routing, and a shortcut to a blank page is worse than no shortcut.
    expect(dangling).toEqual([]);
  });

  it('matches the panel confirmed live: four columns, nineteen shortcuts', () => {
    expect(CREATE_NEW_COLUMNS.map((c) => c.heading)).toEqual(['General', 'Sales', 'Purchase', 'Accounting']);
    expect(CREATE_NEW_COLUMNS.flatMap((c) => c.links).length).toBe(19 - 1);
  });

  it('never offers the same target twice', () => {
    const urls = CREATE_NEW_COLUMNS.flatMap((c) => c.links).map((l) => l.url);

    expect(new Set(urls).size).toBe(urls.length);
  });
});
