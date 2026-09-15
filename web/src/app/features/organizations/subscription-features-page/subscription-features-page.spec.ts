import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import {
  SubscriptionPlan,
  TenantSubscription,
} from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { SubscriptionFeaturesPage } from './subscription-features-page';

/**
 * Phase 46 — the three things this screen gained, and the one it deliberately did not.
 *
 * <p>The AI-scan meter looks like the two phase 41 built and means something different: its ceiling
 * refreshes daily and no add-on raises it, so the copy beside it must not send the reader to buy
 * capacity nobody sells. The billing-location row is deliberately <b>not</b> a meter — the reference
 * product does not cap locations, so a bar that filled up would promise a refusal that never comes.
 * And the entitlement-mismatch panel exists because a plan change never touches the entitlement
 * flags (phase 31's rule, kept), which makes a legitimate mismatch possible and, until this phase,
 * invisible.</p>
 */
describe('SubscriptionFeaturesPage', () => {
  let fixture: ComponentFixture<SubscriptionFeaturesPage>;

  const basePlan: SubscriptionPlan = {
    id: 'plan-standard',
    code: 'Standard',
    name: 'Standard',
    description: 'SME tier',
    annualAmount: 20000,
    productQuota: 5000,
    transactionQuota: 50000,
    dailyAiScanQuota: 20,
    includedFeatures: [
      { name: 'Multiple currency', isIncluded: true },
      { name: 'Inventory tracking', isIncluded: true },
    ],
  };

  const baseSubscription: TenantSubscription = {
    organizationId: 'org-1',
    planId: 'plan-standard',
    planName: 'Standard',
    originatedAt: '2026-01-01T00:00:00Z',
    termStartsAt: '2026-01-01T00:00:00Z',
    termEndsAt: '2027-01-01T00:00:00Z',
    isTrialActive: true,
    daysRemaining: 200,
    subscriptionAmount: 20000,
    irdVerified: false,
    irdSyncEnabled: false,
    allowanceYearStartsAt: '2026-01-01T00:00:00Z',
    allowanceYearEndsAt: '2027-01-01T00:00:00Z',
    usage: {
      transactionsUsed: 10,
      transactionQuota: 50000,
      productsUsed: 2,
      productQuota: 5000,
      aiScansUsed: 3,
      dailyAiScanQuota: 20,
      locationsUsed: 1,
      locationQuota: 0,
    },
    features: [],
    entitlementMismatches: [],
  };

  async function render(overrides: Partial<TenantSubscription>): Promise<void> {
    const subscription = { ...baseSubscription, ...overrides };

    await TestBed.configureTestingModule({
      imports: [SubscriptionFeaturesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'org-1' } } },
        },
        {
          provide: OrganizationsService,
          useValue: {
            getSubscription: () => of(subscription),
            getSubscriptionPlans: () => of([basePlan]),
            setSubscription: () => of(subscription),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionFeaturesPage);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  it('shows the daily AI scan allowance against its ceiling', async () => {
    await render({});

    expect(text()).toContain('AI scans today');
    expect(text()).toContain('3 of 20');
  });

  /**
   * The point of the axis having its own copy. Every tier grants the same 20 and the add-on
   * collection sells none, so "record a term on a larger plan" — which is the right advice for the
   * other two meters — is false here.
   */
  it('does not offer a larger plan as the remedy for a spent scan allowance', async () => {
    await render({
      usage: { ...baseSubscription.usage, aiScansUsed: 20 },
    });

    expect(text()).toContain('no add-on for more');
    expect(text()).toContain('resets at midnight');
  });

  it('treats a zero scan quota as no limit rather than an exhausted one', async () => {
    await render({
      usage: { ...baseSubscription.usage, aiScansUsed: 40, dailyAiScanQuota: 0 },
    });

    expect(text()).toContain('40 — no limit on this plan');
  });

  /**
   * The assertion that records phase 46's live finding. Cadehi runs three billing locations on a
   * plain Enabled flag with an unbounded list and no charge shown anywhere, so holding more than
   * were purchased is reported and never refused.
   */
  it('reports more locations in use than purchased without calling it a limit', async () => {
    await render({
      usage: { ...baseSubscription.usage, locationsUsed: 3, locationQuota: 1 },
    });

    expect(text()).toContain('3 in use, 1 recorded as purchased');
    expect(text()).toContain('Nothing is blocked');
  });

  it('omits the purchased count when none has been recorded', async () => {
    await render({
      usage: { ...baseSubscription.usage, locationsUsed: 2, locationQuota: 0 },
    });

    expect(text()).toContain('2 in use');
    expect(text()).not.toContain('recorded as purchased');
  });

  /**
   * Phase 41 carried item #5. Both directions are shown because the reader's next step differs, and
   * neither is an error — the panel says so, because a screen that lists a difference without
   * saying it is expected reads as a fault report.
   */
  it('surfaces an entitlement the tenant holds that the plan does not include', async () => {
    await render({
      entitlementMismatches: [
        { feature: 'Manufacturing', displayName: 'Manufacturing', heldButNotIncluded: true },
      ],
    });

    expect(text()).toContain('In use here, but not part of this plan');
    expect(text()).toContain('Manufacturing');
    expect(text()).toContain('shown rather than corrected');
  });

  it('surfaces an entitlement the plan includes that the tenant does not hold', async () => {
    await render({
      entitlementMismatches: [
        { feature: 'TrackInventory', displayName: 'Track Inventory', heldButNotIncluded: false },
      ],
    });

    expect(text()).toContain('Part of this plan, but not switched on here');
    expect(text()).toContain('cannot be changed afterwards');
  });

  /** No mismatch, no panel: a heading that is always present, reading "nothing to report", is noise
   *  on a screen a user opens to check one number. */
  it('renders no mismatch panel when the plan and the tenant agree', async () => {
    await render({ entitlementMismatches: [] });

    expect(text()).not.toContain('Features that differ from this plan');
  });
});
