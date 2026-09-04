import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';

import { TenantFeatureKey, TenantSubscription } from './organizations.models';
import { OrganizationsService } from './organizations.service';

/**
 * Phase 27b -- the client half of Phase 20f's feature gate (FR-2.6).
 *
 * <p>20f built the server gate (`FeatureGateBehavior` -> 403) and hid the three Inventory links on
 * the dashboard, but a typed or bookmarked URL still opened the page: it rendered, fired its
 * queries, and showed a wall of 403 errors. This guard makes the route agree with the nav.</p>
 *
 * <p><b>It is worth building, and 20f's own finding is why that needed checking.</b> 20f found only
 * two of the seven flags had any surface to gate -- TrackInventory and MultipleWarehouses -- and
 * explicitly warned against inventing generality for the other five. Phase 25 then shipped
 * Manufacturing, which now has 24 feature-gated requests and six routes of its own. So there are
 * three real cases, not one, and the guard earns its keep. The remaining four flags still have no
 * surface, and no route here pretends otherwise.</p>
 *
 * <p><b>Fails closed, and redirects rather than blocking.</b> A user who lands on a disabled
 * feature's URL goes to the organization dashboard, which is the screen that explains what this
 * tenant does have. Returning plain `false` would leave them on a blank page with the old URL in
 * the bar.</p>
 */
export const featureGuard = (feature: TenantFeatureKey): CanActivateFn => (route: ActivatedRouteSnapshot) => {
  const router = inject(Router);
  const organizationId = organizationIdOf(route);

  if (!organizationId) {
    return router.createUrlTree(['/organizations']);
  }

  return subscriptionFor(organizationId, inject(OrganizationsService)).pipe(
    map((subscription) =>
      subscription?.features.find((x) => x.feature === feature)?.isEnabled === true
        ? true
        : router.createUrlTree(['/organizations', organizationId, 'home'])),
  );
};

/**
 * The organization id lives on an ancestor route (`organizations/:id/...`), so `paramMap` on the
 * activated snapshot alone can miss it depending on where the guard is attached. Walking up is the
 * reliable read.
 */
function organizationIdOf(route: ActivatedRouteSnapshot): string | null {
  for (let current: ActivatedRouteSnapshot | null = route; current; current = current.parent) {
    const id = current.paramMap.get('id');
    if (id) {
      return id;
    }
  }

  return null;
}

/**
 * One in-flight request per organization, shared and replayed. Without this, navigating between two
 * gated routes (Stock Position -> Inventory Ledger) would re-fetch the subscription every time, and
 * a guard is the one place in an app where an extra round trip is felt directly as a slow
 * navigation. Cleared per organization id, so switching tenants never reads a stale entitlement.
 */
const cache = new Map<string, Observable<TenantSubscription | null>>();

function subscriptionFor(
  organizationId: string,
  organizations: OrganizationsService,
): Observable<TenantSubscription | null> {
  const hit = cache.get(organizationId);
  if (hit) {
    return hit;
  }

  const request = organizations.getSubscription(organizationId).pipe(
    // Fail closed: an unreadable subscription must not open a gated route. The redirect target is
    // always reachable, so this cannot strand the user.
    catchError(() => of(null)),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  cache.set(organizationId, request);
  return request;
}

/** Exposed for tests, and for a future "features changed" flow to call. */
export function clearFeatureGuardCache(): void {
  cache.clear();
}
