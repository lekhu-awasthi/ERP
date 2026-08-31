import { SlicePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { TenantSubscription } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * Phase 20f (FR-2.6) -- the read-only view of the tenant's plan and its opted-in Accounting
 * Features. Deliberately has no toggles: confirm-live against the reference product found its own
 * Configurations > Tigg Subscriptions screen renders the entitlements as plain read-only rows, and
 * a disabled feature's panel on Organization > Features carries a static "Disabled" pill plus a
 * banner telling you to contact vendor support -- the only user-operable switch on that whole page
 * belongs to Multi-Currency, which is not a subscription entitlement there at all. This codebase
 * has no vendor-support channel, so the flags stay immutable after Organization creation.
 *
 * Presentation follows the reference product's own shown-but-disabled pattern rather than hiding
 * a disabled feature: each feature keeps its card and description, and a disabled one gains an
 * explanatory note. See docs/phase-20f-status.md.
 */
@Component({
  selector: 'app-subscription-features-page',
  imports: [RouterLink, SlicePipe],
  templateUrl: './subscription-features-page.html',
})
export class SubscriptionFeaturesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly subscription = signal<TenantSubscription | null>(null);

  protected readonly enabledCount = computed(
    () => this.subscription()?.features.filter((x) => x.isEnabled).length ?? 0,
  );

  constructor() {
    this.load();
  }

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getSubscription(this.organizationId).subscribe({
      next: (result) => {
        this.subscription.set(result);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load subscription details.');
      },
    });
  }
}
