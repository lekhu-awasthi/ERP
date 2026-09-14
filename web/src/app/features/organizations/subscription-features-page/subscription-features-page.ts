import { DecimalPipe, SlicePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SubscriptionPlan, TenantSubscription } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { SubscriptionStore } from '../../../shared/platform/subscription.store';

/**
 * Phase 20f (FR-2.6) -- the read-only view of the tenant's plan and its opted-in Accounting
 * Features. The entitlement list deliberately has no toggles: confirm-live against the reference
 * product found its Configurations > Tigg Subscriptions screen renders them as plain read-only rows,
 * and a disabled feature's panel carries a static "Disabled" pill plus a banner telling you to
 * contact vendor support. This codebase has no vendor-support channel, so the flags stay immutable
 * after Organization creation.
 *
 * Phase 31 added the one control this page had: Renew, taking a free-text plan name and a date.
 *
 * Phase 41 replaced that free text with the vendor's own plan catalogue, and made the three fields
 * phase 33 recorded as dead -- Subscription Amount, the two quotas, IRD Verified -- real. They were
 * never dead: both tenants phase 33 read were free trials, and the published price list
 * (tiggapp.com/pricing) sells three tiers whose entire commercial difference is exactly those
 * fields. See docs/phase-41-status.md.
 *
 * The usage meters and the server's quota gate read the same numbers, by construction -- the API
 * returns usage from the reader `SubscriptionQuotaBehavior` blocks on, so this screen cannot show
 * headroom that an Approve then refuses.
 */
@Component({
  selector: 'app-subscription-features-page',
  imports: [RouterLink, SlicePipe, DecimalPipe, AmountPipe, BsDateInput, StatusBanner],
  templateUrl: './subscription-features-page.html',
})
export class SubscriptionFeaturesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly store = inject(SubscriptionStore);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly subscription = signal<TenantSubscription | null>(null);
  protected readonly plans = signal<SubscriptionPlan[]>([]);

  protected readonly enabledCount = computed(
    () => this.subscription()?.features.filter((x) => x.isEnabled).length ?? 0,
  );

  protected readonly renewing = signal(false);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly renewPlanId = signal<string>('');
  protected readonly renewEndsAt = signal('');
  protected readonly showAdvanced = signal(false);

  /** Blank means "take the plan's published figure", which is what the API's null does. */
  protected readonly renewAmount = signal<string>('');
  protected readonly renewProductQuota = signal<string>('');
  protected readonly renewTransactionQuota = signal<string>('');
  protected readonly renewIrdVerified = signal(false);

  /** The catalogue row currently chosen in the picker, for the tick-list preview beneath it. */
  protected readonly selectedPlan = computed(
    () => this.plans().find((x) => x.id === this.renewPlanId()) ?? null,
  );

  protected readonly transactionMeter = computed(() =>
    this.meter(this.subscription()?.usage.transactionsUsed, this.subscription()?.usage.transactionQuota),
  );

  protected readonly productMeter = computed(() =>
    this.meter(this.subscription()?.usage.productsUsed, this.subscription()?.usage.productQuota),
  );

  /**
   * A quota of 0 is the "not metered" sentinel, not a ceiling of nothing -- phase 31's credit limit,
   * confirmed live there. Drawing a full bar for it would tell every trial tenant it was out of
   * allowance, which is the single worst thing this screen could say.
   */
  private meter(used: number | undefined, quota: number | undefined): SubscriptionMeter {
    if (!quota) {
      return { metered: false, used: used ?? 0, quota: 0, percent: 0, tone: 'bg-secondary' };
    }

    const consumed = used ?? 0;
    const percent = Math.min(Math.round((consumed / quota) * 100), 100);

    return {
      metered: true,
      used: consumed,
      quota,
      percent,
      // 90% is where the warning starts, which is also where the shell's banner speaks up: the point
      // of a ceiling a tenant cannot raise themselves is that they need lead time to buy an add-on.
      tone: percent >= 100 ? 'bg-danger' : percent >= 90 ? 'bg-warning' : 'bg-success',
    };
  }

  protected toggleAdvanced(): void {
    this.showAdvanced.update((x) => !x);
  }

  protected renew(): void {
    const endsAt = this.renewEndsAt();

    if (!endsAt) {
      this.errorMessage.set('Choose an end date.');
      return;
    }

    const planId = this.renewPlanId() || null;

    this.renewing.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    // The date input gives a local calendar day; the API stores an instant, so it is sent as the
    // end of that day in UTC -- a subscription that "ends on the 30th" is live all through the 30th.
    // Through the store, not the service: the shell banner reads the same signal, so recording a
    // term updates the warning above this page rather than leaving it saying "trial" (phase 34b's
    // rule -- anything global a screen both shows and changes must reload when it changes).
    this.store
      .save(this.organizationId, {
        planId,
        endsAt: `${endsAt}T23:59:59Z`,
        subscriptionAmount: this.optionalNumber(this.renewAmount()),
        productQuota: this.optionalNumber(this.renewProductQuota()),
        transactionQuota: this.optionalNumber(this.renewTransactionQuota()),
        irdVerified: this.renewIrdVerified(),
      })
      .subscribe({
        next: (result) => {
          this.renewing.set(false);
          this.applySubscription(result);
          this.successMessage.set(`Subscription recorded on the ${result.planName} plan.`);
        },
        error: (err: unknown) => {
          this.renewing.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update the subscription.');
        },
      });
  }

  /** An empty box means "leave it to the plan"; only a real number is sent. */
  private optionalNumber(raw: string): number | undefined {
    const trimmed = raw.trim();

    if (!trimmed) {
      return undefined;
    }

    const parsed = Number(trimmed);

    return Number.isFinite(parsed) ? parsed : undefined;
  }

  constructor() {
    this.load();
  }

  private load(): void {
    this.loading.set(true);

    this.organizationsService.getSubscriptionPlans(this.organizationId).subscribe({
      next: (result) => this.plans.set(result),
      // A catalogue that will not load leaves the picker empty, which still allows an unmetered
      // trial term to be recorded. It is not worth failing the whole screen for.
      error: () => this.plans.set([]),
    });

    // A direct read rather than the store's cached signal: this screen must show usage as of now,
    // and it is the one place where a figure minutes old would be misleading. The save path below
    // still goes through the store, which is what keeps the shell banner in step.
    this.organizationsService.getSubscription(this.organizationId).subscribe({
      next: (result) => {
        this.applySubscription(result);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load subscription details.');
      },
    });
  }

  private applySubscription(result: TenantSubscription): void {
    this.subscription.set(result);
    this.renewPlanId.set(result.planId ?? '');
    this.renewEndsAt.set(result.trialEndsAt.slice(0, 10));
    this.renewIrdVerified.set(result.irdVerified);
    this.renewAmount.set('');
    this.renewProductQuota.set('');
    this.renewTransactionQuota.set('');
  }
}

interface SubscriptionMeter {
  metered: boolean;
  used: number;
  quota: number;
  percent: number;
  tone: string;
}
