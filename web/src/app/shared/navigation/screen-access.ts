import { Route } from '@angular/router';

import { anyFeatureKeysOf, featureKeyOf } from '../../core/organizations/feature.guard';
import { TenantFeatureKey, TenantSubscription } from '../../core/organizations/organizations.models';
import { permissionKeysOf } from '../../core/organizations/permission.guard';
import { GrantState } from '../../core/platform/my-permissions.store';

/**
 * What a screen needs before the shell offers it: the tenant's entitlements and the user's grants.
 *
 * **Read off the route's own guards, never written down beside them.** `featureGuard`,
 * `anyFeatureGuard` and `permissionGuard` each stamp what they check on themselves, and this is the
 * only reader. So the nav cannot offer a screen its guard would bounce, which is what happened from
 * phase 34b until now: the nav was derived from the route table's *paths* only, so a tenant without
 * Multiple Warehouses was offered Warehouse Transfers, and a restricted role was offered every screen
 * in the app, each of which then redirected Home or showed a wall of 403s.
 */
export interface ScreenRequirement {
  /** Every group must be satisfied; a group is satisfied by any one of its entitlements. */
  readonly features: readonly (readonly TenantFeatureKey[])[];
  /** Any one of these keys opens the screen. Empty means the route declares no key. */
  readonly permissions: readonly string[];
}

export const NO_REQUIREMENT: ScreenRequirement = { features: [], permissions: [] };

export function requirementOf(route: Route): ScreenRequirement {
  const features: (readonly TenantFeatureKey[])[] = [];
  const permissions: string[] = [];

  for (const guard of route.canActivate ?? []) {
    const single = featureKeyOf(guard);
    if (single) {
      features.push([single]);
    }

    const any = anyFeatureKeysOf(guard);
    if (any) {
      features.push(any);
    }

    permissions.push(...(permissionKeysOf(guard) ?? []));
  }

  return { features, permissions };
}

/**
 * Whether the shell should offer a screen.
 *
 * Entitlements fail **closed** (an unread or unreadable subscription hides a gated screen), exactly as
 * `featureGuard` does, so the two agree. Grants fail **open** once the read has failed, as
 * `permissionGuard` does, but stay hidden while it is still loading so a link never appears and then
 * vanishes. See `MyPermissionsStore` for why.
 */
export function isOffered(
  requirement: ScreenRequirement,
  subscription: TenantSubscription | null,
  granted: GrantState,
): boolean {
  const enabled = new Set(subscription?.features.filter((x) => x.isEnabled).map((x) => x.feature) ?? []);

  if (!requirement.features.every((group) => group.some((feature) => enabled.has(feature)))) {
    return false;
  }

  if (requirement.permissions.length === 0 || granted === 'unknown') {
    return true;
  }

  if (granted === 'loading') {
    return false;
  }

  return requirement.permissions.some((key) => granted.has(key));
}
