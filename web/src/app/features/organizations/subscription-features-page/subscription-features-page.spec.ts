import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import {
  SetTenantSubscriptionRequest,
  SubscriptionPlan,
  TenantSubscription,
} from '../../../core/organizations/organizations.models';
import { CrmService } from '../../../core/crm/crm.service';
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
  let savedRequest: SetTenantSubscriptionRequest | null = null;

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
    isActive: true,
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

  /**
   * Phase 49 — `smsBalance` of `null` stands for the read failing, which is what a role without
   * `Crm.SmsCreditLedger.View` gets. That role holds `Tenancy.Subscription.View` (every role does,
   * because the shell reads it), so the two are genuinely separable and the screen has to cope.
   */
  async function render(
    overrides: Partial<TenantSubscription>,
    smsBalance: number | null = 250,
  ): Promise<void> {
    const subscription = { ...baseSubscription, ...overrides };
    savedRequest = null;

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
            setSubscription: (_id: string, request: SetTenantSubscriptionRequest) => {
              savedRequest = request;
              return of(subscription);
            },
          },
        },
        {
          provide: CrmService,
          useValue: {
            listSmsCreditLedger: () =>
              smsBalance === null
                ? throwError(() => new Error('forbidden'))
                : of({ balance: smsBalance, rows: [], page: 1, pageSize: 1, totalCount: 0 }),
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

  /**
   * Phase 49 (phase 46 limitation #7). SMS is the one metered axis this screen had no row for, and
   * the reason it is a balance rather than a meter is the reason it was missing: it is bought and
   * spent down, not granted per term. Shown, and shown as what it is.
   */
  it('shows the SMS credit balance as a balance rather than an allowance', async () => {
    await render({});

    expect(text()).toContain('SMS credits');
    expect(text()).toContain('250 remaining');
    expect(text()).toContain('not an allowance for this term');
  });

  /**
   * The permission boundary stays where phase 18 put it: the balance is read through the SMS
   * module's own query, so a role without `Crm.SmsCreditLedger.View` gets no row at all rather than
   * a zero it would read as "out of credits".
   */
  it('renders no SMS row when the ledger read is refused', async () => {
    await render({}, null);

    expect(text()).not.toContain('SMS credits');
  });

  /**
   * Phase 49 — which day does a term end on.
   *
   * <p>The stored value is an instant and the box holds a calendar day, so the two have to agree
   * about the clock. `2027-01-01T23:59:59Z` is 05:44 on the 2nd in Kathmandu, which is the day
   * `NepaliDatePipe` correctly renders — so before this phase the box said the 1st while any other
   * rendering of the same value said the 2nd. Both halves are asserted here, because fixing only the
   * read would ratchet the term forward one day on every save.</p>
   */
  it('prefills the end date as the Nepal day of the stored instant', async () => {
    await render({ termEndsAt: '2027-01-01T23:59:59Z' });

    const input = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLInputElement>('#subscription-features-page-ends-on');

    expect(input?.value).toBe('2027-01-02');
  });

  it('sends the chosen day as the end of that day on the Nepal wall clock', async () => {
    await render({ termEndsAt: '2027-01-01T23:59:59Z' });

    const save = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.trim() === 'Save');
    save!.click();
    await fixture.whenStable();

    expect(savedRequest?.endsAt).toBe('2027-01-02T23:59:59+05:45');
  });

  /** No mismatch, no panel: a heading that is always present, reading "nothing to report", is noise
   *  on a screen a user opens to check one number. */
  it('renders no mismatch panel when the plan and the tenant agree', async () => {
    await render({ entitlementMismatches: [] });

    expect(text()).not.toContain('Features that differ from this plan');
  });
});
