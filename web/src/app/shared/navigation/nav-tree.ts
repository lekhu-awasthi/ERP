import { QuickLinkDto } from '../../core/platform/platform.models';

/**
 * Phase 34b — the third view of the same derivation.
 *
 * Phase 33 built {@link ./navigation-catalog} for global search and the Quick Links tray; 34a added
 * `PageTitleStrategy` as its second consumer. The left nav is the third, and it is built from
 * `buildCatalog`'s output rather than from a nav tree written down beside it — phase-33 lesson (d),
 * in the one place the catalogue exists to serve. The consequence is the point: a route a later
 * phase adds appears in the nav with no edit here, and one it deletes disappears from it.
 *
 * **What is written down here is ordering, not membership.** {@link AREA_ORDER} says which area
 * comes first; it never says which screens are in one. Membership is still `areaOf`, so a new route
 * cannot be silently missing from the nav — at worst it lands in an area that sorts last.
 */

/** One top-level nav row: either a leaf (`items` empty, `url` set) or an accordion group. */
export interface NavGroup {
  readonly area: string;
  /** Set only for a leaf — the single screen the nav row navigates to. */
  readonly url: string | null;
  readonly items: readonly QuickLinkDto[];
}

/**
 * The reference product's own order — Home, CRM, Workflow, Sales, Purchase, Accounting, Inventory,
 * Reports, Configurations (confirmed live 2026-09-10) — with Manufacturing inserted after Inventory,
 * which this codebase has and that tenant does not. An area not named here sorts last,
 * alphabetically, so an area a later phase invents is visible rather than dropped.
 */
export const AREA_ORDER: readonly string[] = [
  'CRM',
  'Workflow',
  'Sales',
  'Purchase',
  'Accounting',
  'Inventory',
  'Manufacturing',
  'Reports',
  'Configurations',
  'Organization',
];

/**
 * Areas the nav renders as a **single leaf** rather than an accordion, with the screen it points at.
 *
 * Reports is the only one, and it is not a style choice: this codebase has **52** routes under
 * `reports/`, more nav rows than every other area put together. Confirmed live — the reference
 * product's Reports is likewise one leaf (`ant-menu-item`, not `ant-menu-submenu`) opening a
 * catalogue page at `#/reports/new` that groups its reports under Accounting / Receivable / Payable
 * / Sales / Purchase / Tax / Inventory headings. {@link REPORT_CATEGORIES} is that grouping, and it
 * is the one thing in this file a route cannot supply on its own.
 */
export const LEAF_AREAS: Readonly<Record<string, string>> = {
  Reports: '/reports',
};

/**
 * Home is excluded from the catalogue — you cannot pin the page you are already on — but it is the
 * nav's first row, exactly as it is the reference product's.
 */
export const HOME_ITEM: QuickLinkDto = { name: 'Home', area: 'Home', kind: 'List', url: '/home' };

/** The Reports index page's headings, in display order. */
export const REPORT_CATEGORY_ORDER: readonly string[] = [
  'Accounting',
  'Receivable',
  'Payable',
  'Sales',
  'Purchase',
  'Tax & Statutory',
  'Inventory',
  'Manufacturing',
];

/**
 * Which family each report belongs to. A report's route says nothing about this, so it is a real
 * mapping rather than a derivation — and `nav-tree.spec.ts` fails when a `reports/` route appears in
 * neither this map nor an exclusion, which is the sweep-guard shape phase-27a established: a new
 * report forces a decision instead of vanishing off the bottom of the index.
 */
export const REPORT_CATEGORIES: Readonly<Record<string, string>> = {
  'transaction-list': 'Accounting',
  'journal-report': 'Accounting',
  'general-ledger-summary': 'Accounting',
  'detail-general-ledger': 'Accounting',
  'general-ledger-master': 'Accounting',
  'trial-balance': 'Accounting',
  'income-statement': 'Accounting',
  'balance-sheet': 'Accounting',
  'cash-flow-summary': 'Accounting',
  'ratio-analysis': 'Accounting',
  'net-trading-assets': 'Accounting',
  'exceptional-report': 'Accounting',
  'system-audit': 'Accounting',
  'user-log': 'Accounting',

  'customer-receivable-summary': 'Receivable',
  'customer-ageing-summary': 'Receivable',
  'invoice-age': 'Receivable',
  'customer-statement': 'Receivable',

  'supplier-payable-summary': 'Payable',
  'supplier-ageing-summary': 'Payable',
  'purchase-bill-age': 'Payable',
  'supplier-statement': 'Payable',

  'sales-by-customer': 'Sales',
  'sales-by-item': 'Sales',
  'sales-by-customer-monthly': 'Sales',
  'sales-by-item-monthly': 'Sales',
  'sales-master-report': 'Sales',
  'sales-summary': 'Sales',

  'purchase-by-supplier': 'Purchase',
  'purchase-by-item': 'Purchase',
  'purchase-by-supplier-monthly': 'Purchase',
  'purchase-by-item-monthly': 'Purchase',
  'purchase-master-report': 'Purchase',

  'sales-register': 'Tax & Statutory',
  'sales-return-register': 'Tax & Statutory',
  'purchase-register': 'Tax & Statutory',
  'purchase-return-register': 'Tax & Statutory',
  'migrated-sales-register': 'Tax & Statutory',
  'migrated-purchase-register': 'Tax & Statutory',
  'vat-summary': 'Tax & Statutory',
  'tds-report': 'Tax & Statutory',
  'annex-thirteen': 'Tax & Statutory',
  'annex-five': 'Tax & Statutory',

  'inventory-position': 'Inventory',
  'stock-ageing': 'Inventory',
  'inventory-movement': 'Inventory',
  'inventory-ledger': 'Inventory',
  'inventory-master': 'Inventory',
  'product-profitability': 'Inventory',

  'production-planning': 'Manufacturing',
  'production-summary': 'Manufacturing',
  'production-variance': 'Manufacturing',
};

/**
 * Groups the catalogue into the nav's top-level rows. `List` entries only — an `Add` target is a
 * form, not a screen you navigate to from a nav rail, and the Create New flyout is where those live.
 */
export function buildNavTree(entries: readonly QuickLinkDto[]): NavGroup[] {
  const byArea = new Map<string, QuickLinkDto[]>();

  for (const entry of entries) {
    if (entry.kind !== 'List') {
      continue;
    }

    const bucket = byArea.get(entry.area);

    if (bucket) {
      bucket.push(entry);
    } else {
      byArea.set(entry.area, [entry]);
    }
  }

  const groups: NavGroup[] = [...byArea].map(([area, items]) => {
    const leafUrl = LEAF_AREAS[area];

    return leafUrl
      ? { area, url: leafUrl, items: [] }
      : { area, url: null, items: [...items].sort((a, b) => a.name.localeCompare(b.name)) };
  });

  const rank = (area: string): number => {
    const index = AREA_ORDER.indexOf(area);
    return index === -1 ? AREA_ORDER.length : index;
  };

  return groups.sort((a, b) => rank(a.area) - rank(b.area) || a.area.localeCompare(b.area));
}

/** The Reports index page's content: every `reports/` screen under its heading, headings in order. */
export function buildReportIndex(
  entries: readonly QuickLinkDto[],
): { category: string; reports: QuickLinkDto[] }[] {
  const byCategory = new Map<string, QuickLinkDto[]>();

  for (const entry of entries) {
    if (entry.area !== 'Reports' || entry.kind !== 'List') {
      continue;
    }

    const category = REPORT_CATEGORIES[entry.url.slice('/reports/'.length)];

    if (!category) {
      continue;
    }

    const bucket = byCategory.get(category);

    if (bucket) {
      bucket.push(entry);
    } else {
      byCategory.set(category, [entry]);
    }
  }

  return REPORT_CATEGORY_ORDER.filter((c) => byCategory.has(c)).map((category) => ({
    category,
    reports: byCategory.get(category)!.sort((a, b) => a.name.localeCompare(b.name)),
  }));
}

/**
 * Which nav group a url belongs in, so the shell can mark the active row and open its accordion.
 *
 * Confirmed live: the reference product marks **both** the leaf (`ant-menu-item-selected`) and its
 * parent group (`ant-menu-submenu-selected`), and the open group survives a full page reload with
 * nothing in `localStorage` — so expansion is **derived from the route, not persisted**. This
 * function is that derivation, and it is why the nav needs no stored state at all.
 */
export function activeGroupOf(tree: readonly NavGroup[], url: string): NavGroup | null {
  const path = pathWithinOrganization(url);

  if (path === null) {
    return null;
  }

  let best: NavGroup | null = null;
  let bestLength = 0;

  for (const group of tree) {
    for (const candidate of group.url ? [group.url] : group.items.map((i) => i.url)) {
      if ((path === candidate || path.startsWith(`${candidate}/`)) && candidate.length > bestLength) {
        best = group;
        bestLength = candidate.length;
      }
    }
  }

  return best;
}

/**
 * The catalogue url of the screen a route is on, or null outside an organization.
 *
 * A record detail url (`/sales/invoices/{guid}`) resolves to its list screen, which is what makes
 * the nav stay marked while you are reading a record — the same reduction phase-33's
 * `history.describe()` makes for the History popover.
 */
export function activeItemUrlOf(tree: readonly NavGroup[], url: string): string | null {
  const path = pathWithinOrganization(url);

  if (path === null) {
    return null;
  }

  const candidates = tree.flatMap((g) => (g.url ? [g.url] : g.items.map((i) => i.url)));

  let best: string | null = null;

  for (const candidate of candidates) {
    if ((path === candidate || path.startsWith(`${candidate}/`)) && candidate.length > (best?.length ?? 0)) {
      best = candidate;
    }
  }

  return best;
}

/** `/organizations/{guid}/sales/invoices?page=2` → `/sales/invoices`; anything else → null. */
function pathWithinOrganization(url: string): string | null {
  const segments = url.split('?')[0].split('#')[0].split('/').filter((s) => s.length > 0);

  if (segments[0] !== 'organizations' || segments.length < 3) {
    return null;
  }

  return `/${segments.slice(2).join('/')}`;
}
