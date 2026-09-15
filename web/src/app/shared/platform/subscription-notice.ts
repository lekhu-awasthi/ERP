import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { TenantSubscription } from '../../core/organizations/organizations.models';
import { SubscriptionStore } from './subscription.store';

/** What the banner is saying, so the tone and the wording cannot disagree. */
export type SubscriptionNoticeKind = 'expired' | 'expiring' | 'trial' | 'quota-exhausted' | 'quota-low';

export interface SubscriptionNoticeState {
  kind: SubscriptionNoticeKind;
  tone: string;
  message: string;
}

/**
 * Phase 41 -- the shell banner that makes a subscription's end foreseeable.
 *
 * <b>Why this exists at all.</b> Before this phase, trial status rendered on exactly one screen:
 * Configurations > Subscription, four clicks deep. Expiry itself is enforced -- phase 31's
 * `SubscriptionExpiryBehavior` refuses every create, edit, approve and void past the end date -- so a
 * tenant's first knowledge of it was a 409 on an invoice they were in the middle of approving. The
 * reference product does not do that: its dashboard carries a persistent
 * "12 days remaining in your trial account. Upgrade now…" banner with a CONTACT US action
 * (confirm-live, Cadehi, 2026-09-10). Phase 41 added two more ceilings that can refuse work, which
 * made one shared warning surface the obvious place for all of it.
 *
 * <b>Always during a trial, only near the edge on a paid plan.</b> That asymmetry is read from the
 * reference tenant rather than invented: its banner was showing at 12 days of a 15-day trial, i.e.
 * from the start. A paying tenant does not need to be told its subscription exists every day.
 *
 * <b>No live-region role, deliberately.</b> Phase 40's rule cuts both ways: a region created already
 * holding its text announces nothing, and `role="alert"` on page furniture that renders on load
 * interrupts whatever is being read. This banner is furniture -- it appears with the page, it is not
 * a response to anything the user just did -- so it is ordinary content in the document order, which
 * a screen reader reaches on its own. `app-status-banner` remains the right tool for the other case.
 */
@Component({
  selector: 'app-subscription-notice',
  imports: [RouterLink],
  templateUrl: './subscription-notice.html',
})
export class SubscriptionNotice {
  private readonly store = inject(SubscriptionStore);

  readonly organizationId = input.required<string>();

  /** Read through the shared store, never fetched here: the Subscription screen changes this state,
   * and a second read of its own would leave the banner stale on the very page that changed it. */
  protected readonly subscription = computed(() => this.store.subscription(this.organizationId())());

  /** Days at which a paid plan starts warning. A trial always warns, so this only governs a plan. */
  private static readonly ExpiryWarningDays = 14;

  /** Where a quota meter turns from information into a warning, matching the Subscription screen. */
  private static readonly QuotaWarningPercent = 90;

  protected readonly notice = computed<SubscriptionNoticeState | null>(() => {
    const sub = this.subscription();

    if (!sub) {
      return null;
    }

    if (!sub.isTrialActive) {
      return {
        kind: 'expired',
        tone: 'alert-danger',
        message:
          'This organization’s subscription has ended. Records can still be read, printed and exported, ' +
          'but no document can be created, edited, approved or voided.',
      };
    }

    const quota = this.quotaNotice(sub);

    // A spent allowance is refusing work right now, which outranks an end date that is still weeks
    // off. An expiring subscription outranks a merely low allowance for the same reason.
    if (quota?.kind === 'quota-exhausted') {
      return quota;
    }

    const days = sub.daysRemaining;
    const onTrial = sub.planId === null;

    if (onTrial || days <= SubscriptionNotice.ExpiryWarningDays) {
      const remaining = `${days} ${days === 1 ? 'day' : 'days'} remaining`;

      return {
        kind: onTrial ? 'trial' : 'expiring',
        tone: days <= 3 ? 'alert-danger' : 'alert-warning',
        // These point at the screen that actually does the thing. The earlier wording named a
        // "Tigg representative" -- copied from the reference product, where a vendor rep really does
        // the selling -- and this codebase models exactly one actor: the tenant's own Admin holds
        // Tenancy.Subscription.Manage. Telling a user to contact someone the system has no concept
        // of, on a banner sitting above a screen where that same user records the term, is a
        // contradiction rather than a courtesy (phase-41 Decision G).
        message: onTrial
          ? `${remaining} in your trial. Record a subscription term under Subscription & Features `
            + 'to set this organization\u2019s plan.'
          : `Your ${sub.planName} subscription ends in ${days} ${days === 1 ? 'day' : 'days'}. `
            + 'Record the new term under Subscription & Features to extend it.',
      };
    }

    return quota;
  });

  private quotaNotice(sub: TenantSubscription): SubscriptionNoticeState | null {
    const worst = [
      { label: 'transactions for this term', used: sub.usage.transactionsUsed, quota: sub.usage.transactionQuota },
      { label: 'products', used: sub.usage.productsUsed, quota: sub.usage.productQuota },
    ]
      // A quota of 0 is the "not metered" sentinel, never a ceiling of nothing -- the single most
      // important reading on this screen, because every trial carries it.
      .filter((x) => x.quota > 0)
      .map((x) => ({ ...x, percent: Math.round((x.used / x.quota) * 100) }))
      .sort((a, b) => b.percent - a.percent)[0];

    if (!worst || worst.percent < SubscriptionNotice.QuotaWarningPercent) {
      return null;
    }

    const exhausted = worst.used >= worst.quota;

    return {
      kind: exhausted ? 'quota-exhausted' : 'quota-low',
      tone: exhausted ? 'alert-danger' : 'alert-warning',
      message: exhausted
        ? `Your ${sub.planName} plan's allowance of ${worst.quota.toLocaleString()} ${worst.label} is used up. ` +
          'Record a term on a larger plan under Subscription & Features to raise it.'
        : `${worst.used.toLocaleString()} of ${worst.quota.toLocaleString()} ${worst.label} used ` +
          `on the ${sub.planName} plan.`,
    };
  }

}
