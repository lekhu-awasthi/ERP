import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { MyPermissionsStore } from '../platform/my-permissions.store';
import { organizationIdOf } from './feature.guard';

/**
 * The route-level counterpart of the server's `IRequirePermission`: the screen opens for a user
 * holding **any** of `keys`, organization-wide or at some billing location.
 *
 * <p><b>The keys are the ones the page's own first read requires</b> (a list's `List...Query`, a
 * report's query, the editor's `Get...Query`), so a user it lets through is not met by a 403 on
 * arrival. A detail route also names its type's Create (or Manage) key, because the same component
 * serves `.../new` (phase 3).</p>
 *
 * <p><b>Usability, not security.</b> The server's `AuthorizationBehavior` still refuses anything not
 * granted; this guard only stops a typed or bookmarked URL from opening a page that would show
 * nothing but errors. For that reason it fails <i>open</i> when the grants cannot be read, unlike
 * {@link featureGuard}, whose subscription read every role has: a role whose own grants are
 * unreadable would otherwise lose every screen at once.</p>
 *
 * <p>The keys are stamped on the guard, as {@link featureGuard} stamps its entitlement, because
 * `NavigationCatalog` reads them to hide the same screen from the nav, search, Quick Links, Create
 * New and the Reports index. One declaration decides both, so they cannot drift.</p>
 */
export const permissionGuard = (...keys: string[]): PermissionCanActivateFn => {
  const guard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
    const router = inject(Router);
    const organizationId = organizationIdOf(route);

    if (!organizationId) {
      return router.createUrlTree(['/organizations']);
    }

    return inject(MyPermissionsStore)
      .load(organizationId)
      .pipe(
        map((granted) =>
          granted === null || keys.some((key) => granted.has(key))
            ? true
            : router.createUrlTree(['/organizations', organizationId, 'home'])),
      );
  };

  return Object.assign(guard, { permissionKeys: keys });
};

/** A `CanActivateFn` that declares which permission keys open its route. */
export type PermissionCanActivateFn = CanActivateFn & { readonly permissionKeys: readonly string[] };

/** Reads the keys a route's {@link permissionGuard} accepts, if it has one. */
export function permissionKeysOf(guard: unknown): readonly string[] | null {
  return typeof guard === 'function' && 'permissionKeys' in guard
    ? ((guard as PermissionCanActivateFn).permissionKeys ?? null)
    : null;
}
