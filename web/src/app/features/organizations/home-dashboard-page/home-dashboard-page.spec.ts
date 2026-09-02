import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { SalesService } from '../../../core/sales/sales.service';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { HomeDashboardPage } from './home-dashboard-page';

/**
 * Phase 23 item 4 / Decision F. Two things are worth pinning about this screen, and neither is the
 * layout: that <b>a card whose query the user cannot run degrades instead of breaking the page</b>
 * (each card rides its own query's permission key -- Decision G), and that the Bank and Cash
 * balance <b>total is suppressed rather than shown as a partial sum</b> when not every account was
 * loaded, which is phase-16c's bug #1 in its natural habitat.
 */
describe('HomeDashboardPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function page(overrides: {
    salesFails?: boolean;
    accountsFails?: boolean;
    accountCount?: number;
    accountTotalCount?: number;
    feedFails?: boolean;
    feedRows?: number;
  } = {}) {
    const feedRows = Array.from({ length: overrides.feedRows ?? 2 }, (_, i) => ({
      date: '2026-09-01',
      documentType: i === 0 ? ('Invoice' as const) : ('Payment' as const),
      documentId: `d${i}`,
      documentCode: `DOC-${i}`,
      contactId: 'c1',
      contactName: 'Acme Traders',
      amount: 250000,
      direction: i === 0 ? null : ('Paid' as const),
    }));
    const recentTransactions = () =>
      overrides.feedFails
        ? throwError(() => new Error('403'))
        : of({ items: feedRows, page: 1, pageSize: 10, totalCount: feedRows.length });
    const accounts = Array.from({ length: overrides.accountCount ?? 2 }, (_, i) => ({
      id: `a${i}`,
      code: `100${i}`,
      name: `Account ${i}`,
      kind: 'Bank',
      bankId: null,
      bankName: null,
      accountNumber: null,
      isActive: true,
      balance: 100000 * (i + 1),
    }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId }) } },
        },
        {
          provide: SalesService,
          useValue: {
            getSalesRegister: () =>
              overrides.salesFails
                ? throwError(() => new Error('403'))
                : of({ totalValue: 1_565_000 }),
          },
        },
        {
          provide: PurchasingService,
          useValue: {
            getPurchaseRegister: () =>
              of({
                totalTaxExemptValue: 0,
                totalTaxableNonCapitalLocalValue: 200_000,
                totalTaxableNonCapitalImportValue: 0,
                totalTaxableCapitalValue: 0,
              }),
          },
        },
        {
          provide: WorkflowService,
          useValue: { getRecentTransactions: recentTransactions },
        },
        {
          provide: AccountingService,
          useValue: {
            getCashFlowSummary: () =>
              of({
                receivedFromCustomerCashIn: 50_000,
                receivedFromCustomerCashOut: 0,
                paidToSupplierCashIn: 0,
                paidToSupplierCashOut: 20_000,
              }),
            listBankAccounts: () =>
              overrides.accountsFails
                ? throwError(() => new Error('403'))
                : of({
                    items: accounts,
                    page: 1,
                    pageSize: 200,
                    totalCount: overrides.accountTotalCount ?? accounts.length,
                  }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(HomeDashboardPage);
    fixture.detectChanges();
    return { fixture, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
  }

  it('renders all four KPI cards from existing queries', () => {
    const { text } = page();

    // Note the labels are uppercased by CSS (text-uppercase), so textContent carries their real
    // casing -- asserting 'SALES' here would fail for a reason that has nothing to do with the card.
    expect(text()).toContain('Sales');
    expect(text()).toContain('Purchase');
    expect(text()).toContain('Receipt');
    expect(text()).toContain('Payment');
  });

  it('formats KPI figures with lakh/crore grouping', () => {
    const { text } = page();

    // 1,565,000 under Western grouping; 15,65,000 under the convention NFR-1.2 requires.
    expect(text()).toContain('15,65,000.00');
    expect(text()).not.toContain('1,565,000.00');
  });

  it('dims a card whose query the user cannot run instead of breaking the page', () => {
    const { text } = page({ salesFails: true });

    expect(text()).toContain('No access');
    // The rest of the dashboard still rendered.
    expect(text()).toContain('2,00,000.00');
    expect(text()).toContain('Bank and Cash Balance');
  });

  it('shows a Total Balance row when every account was loaded', () => {
    const { text } = page({ accountCount: 2 });

    expect(text()).toContain('Total Balance');
    expect(text()).toContain('3,00,000.00'); // 100,000 + 200,000
  });

  it('suppresses the total when more accounts exist than were loaded', () => {
    // phase-16c bug #1: a footer total must cover the whole filtered set, never just the page.
    const { text } = page({ accountCount: 2, accountTotalCount: 250 });

    expect(text()).not.toContain('Total Balance');
    expect(text()).toContain('More accounts exist than are shown here');
  });

  describe('the recent-activity feed', () => {
    it('renders the five tabs the live product shows', () => {
      const { text } = page();

      expect(text()).toContain('Transactions');
      for (const tab of ['All', 'Sales', 'Purchase', 'Payment', 'Receipt']) {
        expect(text()).toContain(tab);
      }
    });

    it('renders rows with lakh-grouped amounts and a per-type label', () => {
      const { text } = page();

      expect(text()).toContain('DOC-0');
      expect(text()).toContain('2,50,000.00');
      expect(text()).toContain('Acme Traders');
      // A Paid Payment reads as "Payment"; a Received one would read as "Receipt".
      expect(text()).toContain('Payment');
    });

    it('shows the live empty state rather than a blank panel', () => {
      const { text } = page({ feedRows: 0 });

      expect(text()).toContain('No Transactions Yet');
      expect(text()).toContain('Create a new transaction to show up here');
    });

    it('degrades on its own when the feed query is refused', () => {
      const { text } = page({ feedFails: true });

      // Same rule as the KPI cards: one refused query dims one panel, never the page.
      expect(text()).toContain('do not have access to recent transactions');
      expect(text()).toContain('Bank and Cash Balance');
      expect(text()).toContain('15,65,000.00');
    });
  });

  it('says so rather than showing zero balances when the account query is denied', () => {
    const { text } = page({ accountsFails: true });

    expect(text()).toContain('do not have access to bank and cash balances');
    expect(text()).not.toContain('Total Balance');
  });
});
