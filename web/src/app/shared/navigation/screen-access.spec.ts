import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Route, provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { TenantFeatureKey, TenantSubscription } from '../../core/organizations/organizations.models';
import { permissionKeysOf } from '../../core/organizations/permission.guard';
import { GrantState, MyPermissionsStore } from '../../core/platform/my-permissions.store';
import { SubscriptionStore } from '../platform/subscription.store';
import { EXCLUDED_PATHS, NavigationCatalog, buildRequirements } from './navigation-catalog';

const ORG = 'org-1';
const PREFIX = 'organizations/:id/';

const ALL_FEATURES: TenantFeatureKey[] = [
  'TrackInventory',
  'MultipleLocations',
  'MultipleWarehouses',
  'MultiCurrency',
  'Manufacturing',
  'PosRetail',
  'PosRestaurant',
];

/**
 * Hubs: screens that declare no key because they show nothing of their own, only links to screens
 * that do. They are offered while something beneath them is. Adding one here is a decision.
 */
const HUBS = ['configuration', 'reports'];

function subscriptionWith(enabled: readonly TenantFeatureKey[]): TenantSubscription {
  return {
    features: ALL_FEATURES.map((feature) => ({
      feature,
      displayName: feature,
      description: '',
      isEnabled: enabled.includes(feature),
    })),
  } as unknown as TenantSubscription;
}

/** Every key any route declares -- a user granted everything. */
const EVERY_KEY = new Set(
  [...buildRequirements(routes).values()].flatMap((requirement) => requirement.permissions),
);

/**
 * The shell offers a screen only when its own route guards would open it. Before this, the left nav
 * (phase 34b), the screen half of search, Quick Links, Create New and the Reports index were all
 * derived from route *paths* alone, so a tenant that never chose Multiple Warehouses was offered
 * Warehouse Transfers, and a restricted role was offered every screen in the app.
 */
describe('NavigationCatalog: offered screens', () => {
  let subscription: WritableSignal<TenantSubscription | null>;
  let granted: WritableSignal<GrantState>;
  let catalog: NavigationCatalog;

  beforeEach(() => {
    subscription = signal(subscriptionWith(ALL_FEATURES));
    granted = signal<GrantState>(EVERY_KEY);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter(routes),
        { provide: SubscriptionStore, useValue: { subscription: () => subscription } },
        { provide: MyPermissionsStore, useValue: { granted: () => granted } },
      ],
    });

    catalog = TestBed.inject(NavigationCatalog);
  });

  function offered(): string[] {
    return catalog.offeredEntries(ORG).map((x) => x.url);
  }

  /**
   * The sweep guard: a screen with no permissionGuard is offered to every role, which is the bug.
   * A new route must name the key its page's first read requires, or be listed with a reason.
   */
  it('gives every in-organization route a permission key, except the excluded screens and hubs', () => {
    const unkeyed = routes
      .filter((r: Route) => (r.path ?? '').startsWith(PREFIX))
      .map((r: Route) => ({ path: r.path!.slice(PREFIX.length), route: r }))
      .filter(({ route }) => !(route.canActivate ?? []).some((g) => permissionKeysOf(g) !== null))
      .map(({ path }) => path)
      .filter((path) => !(path in EXCLUDED_PATHS) && !HUBS.includes(path));

    expect(unkeyed).toEqual([]);
  });

  it('gives each route one permissionGuard at most, since two would mean all-of', () => {
    const doubled = routes
      .filter((r: Route) => (r.canActivate ?? []).filter((g) => permissionKeysOf(g) !== null).length > 1)
      .map((r: Route) => r.path);

    expect(doubled).toEqual([]);
  });

  it('offers everything to a user holding every key on a tenant with every feature', () => {
    expect(offered().length).toBe(catalog.entries().length);
  });

  it('hides Warehouse Transfers without Multiple Warehouses, but keeps the Warehouses list', () => {
    subscription.set(subscriptionWith(ALL_FEATURES.filter((x) => x !== 'MultipleWarehouses')));

    expect(offered()).not.toContain('/inventory/warehouse-transfers');
    expect(offered()).not.toContain('/inventory/warehouse-transfers/new');
    // Multiple Warehouse caps how many there are; it does not switch the screen off (phase 36).
    expect(offered()).toContain('/warehouses');
    expect(offered()).toContain('/inventory/stock-position');
  });

  it('hides every feature-gated screen while the subscription is unknown, as the guard would', () => {
    subscription.set(null);

    expect(offered()).not.toContain('/manufacturing/bills-of-materials');
    expect(offered()).not.toContain('/currencies');
    expect(offered()).toContain('/sales/invoices');
  });

  it('offers a list for its View key and its Add only for the key that creates', () => {
    granted.set(new Set(['Sales.Invoice.View']));

    expect(offered()).toContain('/sales/invoices');
    expect(offered()).not.toContain('/sales/invoices/new');
    expect(offered()).not.toContain('/sales/quotations');

    granted.set(new Set(['Sales.Invoice.View', 'Sales.Invoice.Create']));

    expect(offered()).toContain('/sales/invoices/new');
  });

  it('offers the Reports index only while some report is offered, and only those reports', () => {
    granted.set(new Set(['Sales.Invoice.View']));
    expect(offered()).not.toContain('/reports');

    granted.set(new Set(['Reports.TrialBalance.View']));
    expect(offered()).toContain('/reports');
    expect(offered()).toContain('/reports/trial-balance');
    expect(offered()).not.toContain('/reports/balance-sheet');
  });

  it('hides keyed screens while the grants load, so no link appears and then vanishes', () => {
    granted.set('loading');

    expect(offered()).toEqual([]);
  });

  it('fails open once the grants cannot be read, still applying the features', () => {
    granted.set('unknown');
    subscription.set(subscriptionWith([]));

    expect(offered()).toContain('/sales/invoices');
    expect(offered()).not.toContain('/inventory/stock-position');
  });

  it('searches only offered screens', () => {
    granted.set(new Set(['Contacts.Contact.View']));

    expect(catalog.search(ORG, 'invoice', 10)).toEqual([]);
    expect(catalog.search(ORG, 'contact', 10).map((x) => x.url)).toContain('/contacts');
  });
});
