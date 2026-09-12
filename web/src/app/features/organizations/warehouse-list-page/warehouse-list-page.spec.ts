import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Route, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { routes } from '../../../app.routes';
import { featureKeyOf } from '../../../core/organizations/feature.guard';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { TenantFeatureKey, Warehouse } from '../../../core/organizations/organizations.models';
import { WarehouseListPage } from './warehouse-list-page';

/**
 * Phase 36's opening bug. `MultipleWarehouses` is the one entitlement the server enforces as a
 * <b>cap</b> rather than a block (phase 20f Decision #4): a flag-off tenant still needs its first
 * warehouse, because Invoice and PurchaseBill both require a WarehouseId and nothing seeds one.
 * Phase 27b's route-level `featureGuard` -- written for the all-or-nothing flags -- was stricter
 * than that, bounced such a tenant off this page, and so left it unable to raise either document.
 *
 * Both halves are pinned here: that the route no longer gates the page, and that the cap is instead
 * shown on the page, which is the only place that can tell "capped and at the cap" from "capped and
 * entitled to one more".
 */
describe('WarehouseListPage (MultipleWarehouses is a cap, not a block)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  it('does not feature-gate the warehouses route', () => {
    const route = routes.find((r: Route) => r.path === 'organizations/:id/warehouses');

    expect(route).toBeDefined();

    const gated = (route!.canActivate ?? []).map(featureKeyOf).filter((key) => key !== null);

    // A feature guard here blocks the *first* warehouse as well as the second, which is the bug.
    expect(gated).toEqual([]);
  });

  it('still feature-gates the routes whose entitlement really is all-or-nothing', () => {
    // The sibling routes prove the marker above reads a real guard rather than always returning
    // null -- without which the first test would pass even if the guard were back.
    const gatedPaths = routes
      .filter((r: Route) => (r.canActivate ?? []).some((g) => featureKeyOf(g) !== null))
      .map((r: Route) => r.path);

    expect(gatedPaths).toContain('organizations/:id/currencies');
    expect(gatedPaths).toContain('organizations/:id/billing-locations');
    expect(gatedPaths).not.toContain('organizations/:id/warehouses');
  });

  function page(overrides: { enabled?: boolean; warehouses?: number }) {
    const items: Warehouse[] = Array.from({ length: overrides.warehouses ?? 0 }, (_, i) => ({
      id: `w${i}`,
      organizationId,
      name: `Warehouse ${i}`,
      isActive: true,
      createdAt: '2026-09-01T00:00:00Z',
    }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId }) } },
        },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of(items),
            getSubscription: () =>
              of({
                organizationId,
                planName: 'Trial',
                trialStartsAt: '2026-09-01',
                trialEndsAt: '2026-10-01',
                isTrialActive: true,
                daysRemaining: 20,
                irdSyncEnabled: false,
                features: [
                  {
                    feature: 'MultipleWarehouses' as TenantFeatureKey,
                    displayName: 'Multiple Warehouses',
                    description: '',
                    isEnabled: overrides.enabled ?? true,
                  },
                ],
              }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(WarehouseListPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('offers the form to a flag-off tenant that has no warehouse yet', () => {
    const html = page({ enabled: false, warehouses: 0 });

    // The whole point: without this the tenant can never approve an Invoice or a Purchase Bill.
    expect(html.querySelector('#warehouse-list-page-name')).not.toBeNull();
  });

  it('withdraws the form, with a reason, once a flag-off tenant is at its one warehouse', () => {
    const html = page({ enabled: false, warehouses: 1 });

    expect(html.querySelector('#warehouse-list-page-name')).toBeNull();
    expect(html.textContent).toContain('limited to a single warehouse');
  });

  it('leaves the form in place for an entitled tenant that already has warehouses', () => {
    const html = page({ enabled: true, warehouses: 2 });

    expect(html.querySelector('#warehouse-list-page-name')).not.toBeNull();
    expect(html.textContent).not.toContain('limited to a single warehouse');
  });
});
