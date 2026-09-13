import { Injectable, computed, inject } from '@angular/core';
import { Route, Router } from '@angular/router';

import { QuickLinkDto, QuickLinkKind } from '../../core/platform/platform.models';

/**
 * Phase 33 — every screen in the app, as a navigable target.
 *
 * <b>It is derived from the Router's own config, not written down.</b> The reference product serves
 * this half of its search from the server alongside the record hits; this codebase does not, because
 * the route table, which routes are feature-gated and which are permission-gated are all already
 * client knowledge, and a server-side second copy would be a source of truth that drifts. Deriving
 * it from `Router.config` goes one step further: there is no second copy on this side either, so a
 * route added by a later phase appears in the search and in the Quick Links picker with no edit
 * here, and a route deleted disappears from both. That is the same reasoning phase-30 records as
 * "find the rule, don't sample the list" — here the router *is* the rule.
 *
 * <b>Two entries per list screen.</b> A route with a sibling detail route (`sales/invoices` next to
 * `sales/invoices/:invoiceId`) also yields an Add target at `<list>/new`, because this codebase
 * serves create and edit from one component addressed by a literal `new` (phase-3's routing
 * decision). That mirrors the reference product exactly, whose search offers `Invoice` and
 * `Add Invoice` as separate rows.
 *
 * The display name is the last path segment de-kebabed, with {@link TITLE_OVERRIDES} for the handful
 * the rule names badly. Deriving rather than listing is the point: a name can be wrong and be fixed,
 * but a missing entry is invisible.
 */
@Injectable({ providedIn: 'root' })
export class NavigationCatalog {
  private readonly router = inject(Router);

  /**
   * Every navigable screen, ordered by area then name. Computed once — the route config does not
   * change at runtime.
   */
  readonly entries = computed<QuickLinkDto[]>(() => buildCatalog(this.router.config));

  /** The catalogue filtered to a search term, matched on the screen name and its area. */
  search(term: string, limit: number): QuickLinkDto[] {
    const needle = term.trim().toLowerCase();

    if (needle.length === 0) {
      return [];
    }

    return this.entries()
      .filter((x) => x.name.toLowerCase().includes(needle) || x.area.toLowerCase().includes(needle))
      .slice(0, limit);
  }

  /** Resolves a stored Quick Link back to a router link, prefixed with the organization. */
  routerLink(organizationId: string, link: QuickLinkDto): unknown[] {
    return ['/organizations', organizationId, ...link.url.split('/').filter((x) => x.length > 0)];
  }
}

/** The path prefix every in-organization screen shares. */
const ORG_PREFIX = 'organizations/:id/';

/**
 * Screens deliberately absent from the catalogue, each with the reason — the sweep-guard shape
 * phase-27a established. `navigation-catalog.spec.ts` fails if a route is in neither this list nor
 * the catalogue, so a later phase's new screen forces a decision instead of vanishing.
 */
export const EXCLUDED_PATHS: Readonly<Record<string, string>> = {
  home: 'The dashboard the tray lives on. Pinning a shortcut to the page you are already on is noise.',
  welcome: 'A one-time post-signup landing page, not a screen anyone returns to.',
  search:
    'Phase 39 -- the search results page. A destination you arrive at carrying a term, not a screen: '
    + 'without ?q= it has nothing to show, so offering it in the catalogue or the Quick Links picker '
    + 'would pin a blank page.',
};

/** Areas for the paths whose first segment does not name one. */
const BARE_AREAS: Readonly<Record<string, string>> = {
  payments: 'Sales',
  'quick-payment': 'Accounting',
  'quick-receipt': 'Accounting',
  'allocate-customer-payment': 'Accounting',
  'allocate-supplier-payment': 'Accounting',
  contacts: 'CRM',
  sms: 'CRM',
  products: 'Inventory',
  roles: 'Configurations',
  warehouses: 'Configurations',
  currencies: 'Configurations',
  'billing-locations': 'Configurations',
  features: 'Configurations',
  'lock-date': 'Configurations',
  'organization-profile': 'Configurations',
};

const SEGMENT_AREAS: Readonly<Record<string, string>> = {
  accounting: 'Accounting',
  configuration: 'Configurations',
  contacts: 'CRM',
  // Phase 39 -- the first route under a bare `crm/` segment. Without this its area would de-kebab
  // to "Crm" and the leaf would sit in a group of its own, next to the CRM group it belongs to.
  crm: 'CRM',
  inventory: 'Inventory',
  manufacturing: 'Manufacturing',
  products: 'Inventory',
  purchasing: 'Purchase',
  reports: 'Reports',
  sales: 'Sales',
  workflow: 'Workflow',
};

/** Names the de-kebab rule gets wrong on its own. */
const TITLE_OVERRIDES: Readonly<Record<string, string>> = {
  'accounting/accounts': 'Chart of Accounts',
  'accounting/defaults': 'Accounting Defaults',
  'configuration/general': 'General Settings',
  'configuration/import': 'Import',
  'configuration/migration': 'Migration',
  features: 'Subscription & Features',
  'lock-date': 'Lock Date',
  'organization-profile': 'Organization Profile',
  roles: 'Roles & Permissions',
  sms: 'SMS',
  'reports/annex-five': 'Annex 5',
  'reports/annex-thirteen': 'Annex 13',
  'reports/vat-summary': 'VAT Summary',
  'reports/tds-report': 'TDS Report',
  'reports/system-audit': 'System Audit',
  'reports/user-log': 'User Log',
  'reports/general-ledger-master': 'General Ledger Master',
  'workflow/document-inbox': 'Documents',
};

/** Exported for the guard spec, which builds the catalogue from the same route config. */
export function buildCatalog(routes: readonly Route[]): QuickLinkDto[] {
  const orgPaths = routes
    .map((r) => r.path ?? '')
    .filter((p) => p.startsWith(ORG_PREFIX))
    .map((p) => p.slice(ORG_PREFIX.length));

  const listPaths = orgPaths.filter((p) => !p.includes(':'));
  const detailPaths = new Set(orgPaths.filter((p) => p.includes(':')));

  const entries: QuickLinkDto[] = [];

  for (const path of listPaths) {
    if (path in EXCLUDED_PATHS) {
      continue;
    }

    entries.push(entry(path, 'List', `/${path}`));

    // A sibling `<path>/:someId` route means this list has a detail component, and this codebase
    // addresses its create form as the literal id `new` on that same route.
    const hasDetail = [...detailPaths].some((d) => d.startsWith(`${path}/:`));

    if (hasDetail) {
      entries.push(entry(path, 'Add', `/${path}/new`, `Add ${titleOf(path)}`));
    }
  }

  return entries.sort((a, b) => a.area.localeCompare(b.area) || a.name.localeCompare(b.name));
}

function entry(path: string, kind: QuickLinkKind, url: string, name?: string): QuickLinkDto {
  return { name: name ?? titleOf(path), area: areaOf(path), kind, url };
}

export function areaOf(path: string): string {
  const [head] = path.split('/');
  return SEGMENT_AREAS[head] ?? BARE_AREAS[path] ?? 'Organization';
}

export function titleOf(path: string): string {
  const override = TITLE_OVERRIDES[path];

  if (override) {
    return override;
  }

  const last = path.split('/').at(-1) ?? path;

  return last
    .split('-')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}
