import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { SubscriptionStore } from '../../../shared/platform/subscription.store';

/**
 * Configurations nav hub (roadmap Phase 2 task 5) -- mirrors organization-dashboard-page's card
 * chrome. Links to the lookup-list screens; ReportingTags got its Angular screen in Phase 19
 * (see phase-19-status.md). The remaining lookups (CustomStatus, CustomFieldDefinition) have
 * working APIs (Application/Api layers) but no Angular screen yet -- see phase-2-status.md's
 * scope decisions.
 */
@Component({
  selector: 'app-configuration-shell',
  imports: [RouterLink],
  templateUrl: './configuration-shell.html',
})
export class ConfigurationShell {
  private readonly route = inject(ActivatedRoute);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  private readonly subscription = inject(SubscriptionStore).subscription(this.organizationId);

  /** Phase 60 -- the Point of Sale card shows only where a till can exist, as its route guard allows. */
  protected readonly posEnabled = computed(() =>
    this.subscription()?.features.some(
      (x) => (x.feature === 'PosRetail' || x.feature === 'PosRestaurant') && x.isEnabled) === true);
}
