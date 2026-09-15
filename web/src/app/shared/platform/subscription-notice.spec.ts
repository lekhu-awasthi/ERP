import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { TenantSubscription } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';
import { SubscriptionNotice } from './subscription-notice';
import { SubscriptionStore } from './subscription.store';

/**
 * Phase 41 — the shell banner that makes a subscription's end foreseeable.
 *
 * <p>What is worth pinning here is the <b>reading of zero</b> and the <b>precedence between
 * warnings</b>. Zero is the "not metered" sentinel on both quotas (phase 31's credit limit,
 * confirmed live), so a naive `used / quota` would divide by zero and a naive `used >= quota` would
 * tell every trial tenant — which is every new tenant — that it was out of allowance on day one.
 * That is the single worst thing this banner could say, and it is one character away at all
 * times.</p>
 */
describe('SubscriptionNotice', () => {
  let fixture: ComponentFixture<SubscriptionNotice>;

  /** What the service double's `setSubscription` resolves to. A mutable let rather than an
   * `overrideProvider`, which Angular refuses once the test module has been instantiated. */
  let savedResponse: TenantSubscription | null = null;

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
    usage: { transactionsUsed: 0, transactionQuota: 50000, productsUsed: 0, productQuota: 5000 },
    features: [],
  };

  async function render(overrides: Partial<TenantSubscription>): Promise<void> {
    const subscription = { ...baseSubscription, ...overrides };

    await TestBed.configureTestingModule({
      imports: [SubscriptionNotice],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: OrganizationsService,
          useValue: {
            getSubscription: () => of(subscription),
            setSubscription: () => of(savedResponse ?? subscription),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionNotice);
    fixture.componentRef.setInput('organizationId', 'org-1');
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function bannerText(): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function hasBanner(): boolean {
    return (fixture.nativeElement as HTMLElement).querySelector('.alert') !== null;
  }

  afterEach(() => {
    savedResponse = null;
    TestBed.resetTestingModule();
  });

  it('says nothing to a healthy paid tenant well inside every limit', async () => {
    await render({});

    expect(hasBanner()).toBe(false);
  });

  /**
   * The reference product's own banner was showing at 12 days of a 15-day trial — i.e. from the
   * start — so a trial always warns, however much time is left.
   */
  it('always speaks during a trial, even on day one', async () => {
    await render({ planId: null, planName: 'Trial', daysRemaining: 15, subscriptionAmount: 0 });

    expect(bannerText()).toContain('15 days remaining in your trial');
  });

  /**
   * A trial carries quota 0 on both axes. Read as a ceiling rather than as the sentinel, this is the
   * case that tells every new tenant it is out of allowance before it has done anything.
   */
  it('treats a zero quota as no limit rather than as an exhausted one', async () => {
    await render({
      planId: null,
      planName: 'Trial',
      daysRemaining: 15,
      usage: { transactionsUsed: 0, transactionQuota: 0, productsUsed: 0, productQuota: 0 },
    });

    expect(bannerText()).not.toContain('used up');
    expect(bannerText()).toContain('trial');
  });

  /**
   * <b>Every banner points at a screen, never at a person.</b> The original wording told the user to
   * "contact your Tigg representative" -- copied from the reference product, where a vendor rep does
   * the selling. This codebase models exactly one actor: `Tenancy.Subscription.Manage` is seeded to
   * the tenant's own Admin, so the reader of the banner is the same person who records the term, and
   * the banner sat above a screen that let them do it. Naming an actor the system has no concept of
   * is a contradiction, not a courtesy (phase-41 Decision G), so this asserts in both directions.
   */
  it('directs the reader to the screen that records a term, not to an actor that does not exist', async () => {
    await render({ planId: null, planName: 'Trial', daysRemaining: 15, subscriptionAmount: 0 });

    expect(bannerText()).toContain('Subscription & Features');
    expect(bannerText()).not.toContain('representative');
  });

  it('does the same when a paid term is running out', async () => {
    await render({ daysRemaining: 10, planName: 'Standard' });

    expect(bannerText()).toContain('Subscription & Features');
    expect(bannerText()).not.toContain('representative');
  });

  it('says nothing to a paid tenant a month out', async () => {
    await render({ daysRemaining: 30 });

    expect(hasBanner()).toBe(false);
  });

  it('warns a paid tenant as the end approaches', async () => {
    await render({ daysRemaining: 10 });

    expect(bannerText()).toContain('Standard subscription ends in 10 days');
  });

  it('reports an ended subscription as read-only rather than as expiring', async () => {
    await render({ isTrialActive: false, daysRemaining: 0 });

    expect(bannerText()).toContain('subscription has ended');
    expect(fixture.nativeElement.querySelector('.alert-danger')).not.toBeNull();
  });

  it('warns as an allowance runs low, naming the axis that is closest', async () => {
    await render({
      usage: { transactionsUsed: 4800, transactionQuota: 5000, productsUsed: 1, productQuota: 5000 },
    });

    expect(bannerText()).toContain('transactions for this term');
    expect(bannerText()).toContain('4,800 of 5,000');
  });

  it('says nothing while an allowance is merely in use', async () => {
    await render({
      usage: { transactionsUsed: 2500, transactionQuota: 5000, productsUsed: 0, productQuota: 5000 },
    });

    expect(hasBanner()).toBe(false);
  });

  /**
   * A spent allowance is refusing work right now; an end date weeks away is not. The two are
   * simultaneously true often enough that the order between them has to be decided rather than
   * left to whichever branch happens to come first.
   */
  it('puts a spent allowance ahead of an approaching end date', async () => {
    await render({
      daysRemaining: 5,
      usage: { transactionsUsed: 5000, transactionQuota: 5000, productsUsed: 0, productQuota: 5000 },
    });

    expect(bannerText()).toContain('used up');
    // ...and the exhausted banner points at the same screen as the other two.
    expect(bannerText()).not.toContain('representative');
    expect(bannerText()).not.toContain('ends in 5 days');
  });

  /** An ended subscription outranks everything: nothing else is actionable until it is renewed. */
  it('puts an ended subscription ahead of a spent allowance', async () => {
    await render({
      isTrialActive: false,
      usage: { transactionsUsed: 5000, transactionQuota: 5000, productsUsed: 0, productQuota: 5000 },
    });

    expect(bannerText()).toContain('subscription has ended');
  });

  /**
   * Phase 40's rule in the direction that is easy to get backwards: this banner renders with the
   * page rather than in response to anything the user did, so an assertive live region here would
   * interrupt whatever a screen-reader user was reading, on every navigation.
   */
  it('carries no live-region role, because it is page furniture', async () => {
    await render({ planId: null, planName: 'Trial', daysRemaining: 3 });

    const banner = fixture.nativeElement.querySelector('.alert') as HTMLElement;

    expect(banner).not.toBeNull();
    expect(banner.getAttribute('role')).toBeNull();
    expect(banner.getAttribute('aria-live')).toBeNull();
  });

  it('offers a way through to the subscription screen', async () => {
    await render({ planId: null, planName: 'Trial', daysRemaining: 3 });

    const link = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;

    expect(link.getAttribute('href')).toBe('/organizations/org-1/features');
  });

  /**
   * The defect this phase's own browser pass found, pinned. The banner and the Subscription screen
   * show one fact and only one of them changes it; before the shared store, recording a Standard plan
   * left the banner saying "366 days remaining in your trial" on that very page.
   */
  it('re-renders when a term is recorded through the store', async () => {
    await render({ planId: null, planName: 'Trial', daysRemaining: 20, subscriptionAmount: 0 });
    expect(bannerText()).toContain('trial');

    const store = TestBed.inject(SubscriptionStore);
    const saved: TenantSubscription = {
      ...baseSubscription,
      planId: 'plan-standard',
      planName: 'Standard',
      daysRemaining: 365,
    };

    savedResponse = saved;

    store.save('org-1', { planId: 'plan-standard', endsAt: '2027-09-14T23:59:59Z' }).subscribe();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(hasBanner()).toBe(false);
  });

  /** A banner that cannot load its own subject stays silent rather than breaking every screen. */
  it('stays silent when the subscription cannot be read', async () => {
    await TestBed.configureTestingModule({
      imports: [SubscriptionNotice],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: OrganizationsService,
          useValue: { getSubscription: () => ({ subscribe: ({ error }: { error: () => void }) => error() }) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionNotice);
    fixture.componentRef.setInput('organizationId', 'org-1');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(hasBanner()).toBe(false);
  });
});
