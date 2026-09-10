import { Component, computed, inject, input, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs/operators';

import { OrganizationsService } from '../../core/organizations/organizations.service';
import { NavigationCatalog } from '../navigation/navigation-catalog';
import { NavGroup, activeGroupOf, activeItemUrlOf, buildNavTree, HOME_ITEM } from '../navigation/nav-tree';
import { CreateNewFlyout } from './create-new-flyout';

/**
 * Phase 34b — the left nav the reference product has on every screen and phase 33 deferred by name
 * (its carried item #5).
 *
 * <b>Its shape is confirm-lived, not inferred</b> (2026-09-10). Four behaviours were read off the
 * live tenant and are reproduced here deliberately:
 *
 * 1. **Accordion, one group at a time.** Opening Sales closed CRM — `ant-menu` in `accordion` mode.
 * 2. **Expansion is derived from the route, not persisted.** A hard reload on `#/sales/customers`
 *    came back with Sales open and Customers marked, and there is *no* nav key in that app's
 *    `localStorage` (which does hold its date filter, so the absence is meaningful). That is why
 *    this component stores no preference and needs no server round trip: {@link activeGroupOf} is
 *    the whole mechanism.
 * 3. **Both levels are marked** — the leaf and its parent group.
 * 4. **Change Company is a link, not a switcher.** On that tenant it is a plain anchor to an
 *    external account portal (`me.tiggapp.com/erp/`), so the in-app equivalent is a link to this
 *    app's own organization picker, not a dropdown of tenants.
 *
 * The rows come from {@link NavigationCatalog} — see `nav-tree.ts` for why the tree is derived from
 * the router rather than written down beside it.
 *
 * `aria-expanded` is set by hand on every group toggle: Bootstrap's JavaScript is not loaded
 * anywhere in this app, so nothing sets it for free (phase-34a, extending phase-22's gotcha).
 */
@Component({
  selector: 'app-left-nav',
  imports: [RouterLink, CreateNewFlyout],
  templateUrl: './left-nav.html',
  styleUrl: './left-nav.scss',
})
export class LeftNav {
  readonly organizationId = input.required<string>();

  private readonly catalog = inject(NavigationCatalog);
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);

  protected readonly tree = computed<NavGroup[]>(() => buildNavTree(this.catalog.entries()));
  protected readonly home = HOME_ITEM;

  /** The url of the screen currently shown, kept in a signal so the marking is reactive. */
  private readonly currentUrl = signal(this.router.url);

  /**
   * A group the user opened by hand, overriding the route-derived one until the next navigation.
   *
   * Cleared on every `NavigationEnd`, which is what makes behaviour (2) above fall out: after you
   * navigate, the open group is once again *the one containing the screen you are on*.
   */
  private readonly manuallyOpen = signal<string | null>(null);

  /** Small-viewport drawer state. Above `lg` the rail is always shown and this is ignored. */
  protected readonly drawerOpen = signal(false);

  protected readonly activeGroup = computed(() => activeGroupOf(this.tree(), this.currentUrl()));
  protected readonly activeItemUrl = computed(() => activeItemUrlOf(this.tree(), this.currentUrl()));

  protected readonly openArea = computed(() => this.manuallyOpen() ?? this.activeGroup()?.area ?? null);

  /** The tenant's own name, for the footer. One request, and the footer simply stays blank until it lands. */
  protected readonly organizationName = signal('');

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => {
        this.currentUrl.set(e.urlAfterRedirects);
        this.manuallyOpen.set(null);
        this.drawerOpen.set(false);
      });

    this.organizations.myOrganizations().subscribe({
      next: (result) => {
        const match = result.organizations.find((o) => o.organizationId === this.organizationId());
        this.organizationName.set(match?.name ?? '');
      },
      // A failed lookup costs the footer its name and nothing else; the nav still navigates.
      error: () => this.organizationName.set(''),
    });
  }

  protected isOpen(group: NavGroup): boolean {
    return this.openArea() === group.area;
  }

  protected isActiveItem(url: string): boolean {
    return this.activeItemUrl() === url;
  }

  protected isActiveGroup(group: NavGroup): boolean {
    return this.activeGroup()?.area === group.area;
  }

  /**
   * Accordion: opening a group closes whichever was open, and clicking the open one collapses it.
   *
   * Collapsing stores `''` rather than `null`, because `null` means "no override, use the route's
   * group" — which would immediately re-open the group the user just closed.
   */
  protected toggleGroup(group: NavGroup): void {
    this.manuallyOpen.set(this.isOpen(group) ? '' : group.area);
  }

  protected toggleDrawer(): void {
    this.drawerOpen.update((x) => !x);
  }

  protected linkFor(url: string): unknown[] {
    return ['/organizations', this.organizationId(), ...url.split('/').filter((s) => s.length > 0)];
  }
}
