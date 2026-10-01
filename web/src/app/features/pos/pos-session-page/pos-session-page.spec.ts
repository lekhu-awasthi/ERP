import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { Account } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { AuthService } from '../../../core/auth/auth.service';
import { cartStorageKey } from '../../../core/pos/pos-cart';
import {
  ClosePosSessionRequest,
  PosReceipt,
  PosRefundReceipt,
  PosSession,
  PosSessionRefund,
  PosSessionSale,
  PosTill,
  RecordPosCashMovementRequest,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { PosSessionPage } from './pos-session-page';

/**
 * Phase 62 -- a session's X/Z report, its Cash In/Out and close, and its sales' reprints. On phase
 * 61's E2E numbers: float 1,000, cash sales 933, a cash-out of 100, expected 1,833.
 */
describe('PosSessionPage', () => {
  const organizationId = 'org-1';

  const session = (overrides: Partial<PosSession> = {}): PosSession => ({
    id: 'ses-1', code: 'SES0001', locationId: 'loc-1', locationName: 'Thamel', userId: 'u1', userName: 'Sita',
    status: 'Open', openedAt: '2026-10-01T03:00:00Z', closedAt: null, cashAccountId: 'cash', openingFloat: 1000,
    openingCount: null,
    sales: {
      salesCount: 3, subTotal: 980, serviceCharge: 80, vat: 137.8, roundOff: 0.2, grandTotal: 1198, tenders: [
        { paymentModeId: 'm-cash', paymentModeName: 'Cash', kind: 'Cash', amount: 1300 },
        { paymentModeId: 'm-card', paymentModeName: 'Card', kind: 'Card', amount: 116 },
      ], tendered: 1416, change: 367, settled: 1049, credit: 149, cashSales: 933,
      refunds: { refundsCount: 0, subTotal: 0, serviceCharge: 0, vat: 0, roundOff: 0, grandTotal: 0, payouts: [], paidOut: 0, toAccount: 0, cashRefunds: 0 }, netSales: 1198,
    },
    cashMovements: [], cashIn: 0, cashOut: 0, expectedCash: 1933, countedCash: null, closingCount: null,
    cashDifference: null, closingNote: null,
    ...overrides,
  });

  const sale: PosSessionSale = {
    invoiceId: 'inv-1', code: 'INV0001', status: 'Approved', soldAt: '2026-10-01T04:00:00Z', customerName: 'Cash Customer',
    isWalkIn: true, grandTotal: 633, tendered: 1000, changeAmount: 367, creditAmount: 0, isAbbreviatedTaxInvoice: false,
    printCount: 1,
  };

  const till: PosTill = {
    locationId: 'loc-1', locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail', availableTabs: ['Retail'],
    defaultTab: 'Retail', serviceChargeEnabled: true, serviceChargeRate: 10, roundOffEnabled: true,
    cashVerificationRequired: false, denominations: [1000, 500, 100], printInvoice: true,
    abbreviatedTaxInvoiceEnabled: false, isVatRegistered: true, warehouseId: null, warehouseName: null,
    walkInCustomer: null, paymentModes: [], categories: [], canSellOnCredit: true, printCreditNote: true,
    canRefund: true,
  };

  const account = (id: string, code: string, name: string): Account => ({
    id, organizationId, code, name, rootType: 'Expense', groupId: 'g', kind: 'Other', bankId: null,
    accountNumber: null, isActive: true, createdAt: '2026-10-01T00:00:00Z',
  });

  beforeEach(() => {
    localStorage.clear();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  // Phase 63 -- phase 59's refund: one Coke, 68, paid back in cash, against INV0001.
  const refund: PosSessionRefund = {
    creditNoteId: 'cn-1', code: 'CN0001', status: 'Approved', refundedAt: '2026-10-01T05:00:00Z', invoiceId: 'inv-1',
    invoiceCode: 'INV0001', customerName: 'Cash Customer', isWalkIn: true, reason: 'Damaged can', grandTotal: 68,
    paidOut: 68, toAccount: 0, printCount: 1,
  };

  function page(current: PosSession, userId = 'u1', refunds: PosSessionRefund[] = []) {
    const moved: RecordPosCashMovementRequest[] = [];
    const closed: ClosePosSessionRequest[] = [];
    let state = current;
    const service = {
      getSession: (): Observable<PosSession> => of(state),
      listSessionSales: (): Observable<PosSessionSale[]> => of([sale]),
      listSessionRefunds: (): Observable<PosSessionRefund[]> => of(refunds),
      printRefundReceipt: (): Observable<PosRefundReceipt> =>
        of({ code: 'CN0001', printNumber: 2, lines: [], payouts: [] } as unknown as PosRefundReceipt),
      getTill: (): Observable<PosTill> => of(till),
      recordCashMovement: (_o: string, _s: string, request: RecordPosCashMovementRequest): Observable<PosSession> => {
        moved.push(request);
        state = { ...state, cashOut: request.amount, expectedCash: state.expectedCash - request.amount };
        return of(state);
      },
      closeSession: (_o: string, _s: string, request: ClosePosSessionRequest): Observable<PosSession> => {
        closed.push(request);
        state = { ...state, status: 'Closed', countedCash: request.countedAmount, cashDifference: -9 };
        return of(state);
      },
      printReceipt: (): Observable<PosReceipt> => of({ code: 'INV0001', printNumber: 2, lines: [], tenders: [] } as unknown as PosReceipt),
    };

    TestBed.configureTestingModule({
      imports: [PosSessionPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosService, useValue: service },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([account('cash', '1010', 'Cash In Hand'), account('veg', '5100', 'Vegetables')]) } },
        { provide: AuthService, useValue: { currentUser: () => ({ userId, email: 'sita@example.com' }) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId, sessionId: 'ses-1' }) } },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosSessionPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };

    return {
      element,
      moved,
      closed,
      // Text nodes joined by a space, so a row's <th> and <td> read as two words.
      text: () => {
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        const parts: string[] = [];
        for (let node = walker.nextNode(); node; node = walker.nextNode()) parts.push(node.textContent ?? '');
        return parts.join(' ').replace(/\s+/g, ' ');
      },
      press: (label: string) => {
        [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(label))!.click();
        render();
      },
      type: (id: string, value: string) => {
        const input = element.querySelector<HTMLInputElement | HTMLTextAreaElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        render();
      },
      choose: (id: string, value: string) => {
        const select = element.querySelector<HTMLSelectElement>(`#${id}`)!;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        render();
      },
      submit: (headingId: string) => {
        element.querySelector(`[aria-labelledby="${headingId}"] form`)!.dispatchEvent(new Event('submit'));
        render();
      },
    };
  }

  it('reads the X report from the one session figure, and lists the sales', () => {
    const p = page(session());

    expect(p.text()).toContain('X Report (so far)');
    expect(p.text()).toContain('Total sales 1,198.00');
    expect(p.text()).toContain('Expected in drawer 1,933.00');
    expect(p.text()).toContain('INV0001');
  });

  it('records a cash out naming an account other than the drawer', () => {
    const p = page(session());

    const offered = [...p.element.querySelectorAll<HTMLOptionElement>('#pos-movement-account option')].map((o) => o.value);
    expect(offered).toEqual(['', 'veg']);

    p.type('pos-movement-amount', '100');
    p.choose('pos-movement-account', 'veg');
    p.type('pos-movement-note', 'Vegetables');
    p.submit('pos-cash-heading');

    expect(p.moved).toEqual([{
      direction: 'Out', amount: 100, accountId: 'veg', note: 'Vegetables', overrideNegativeCashBalanceWarning: false,
    }]);
    expect(p.text()).toContain('The drawer should now hold 1,833.00');
  });

  it('will not close a count that differs without a note, and discards this browser’s held carts on close', () => {
    localStorage.setItem(cartStorageKey(organizationId, 'loc-1', 'ses-1'), JSON.stringify({ version: 1, lines: [], holds: [{ id: 'h' }] }));
    const p = page(session({ expectedCash: 1833 }));
    expect(p.text()).toContain('1 held cart(s) in this browser will be discarded');

    p.type('pos-close-amount', '1824');
    expect(p.text()).toContain('Short by 9.00');
    p.submit('pos-close-heading');
    expect(p.closed).toEqual([]);
    expect(p.text()).toContain('Say why in the note.');
    // ...and at the field it names, not only in the banner at the top of a scrolled page (phase 47).
    const note = p.element.querySelector('#pos-close-note')!;
    expect(note.getAttribute('aria-invalid')).toBe('true');
    expect(note.getAttribute('aria-describedby')).toBe('pos-close-note-error');

    p.type('pos-close-note', 'Coins short');
    p.submit('pos-close-heading');

    expect(p.closed).toEqual([{ countedAmount: 1824, denominations: null, note: 'Coins short' }]);
    expect(p.text()).toContain('Session SES0001 is closed.');
    expect(localStorage.getItem(cartStorageKey(organizationId, 'loc-1', 'ses-1'))).toBeNull();
  });

  it('counts refunds in the X report and takes cash refunds out of the drawer figure', () => {
    const s = session();
    const p = page({
      ...s,
      expectedCash: 1865,
      sales: {
        ...s.sales,
        refunds: {
          refundsCount: 1, subTotal: 60, serviceCharge: 0, vat: 7.8, roundOff: 0.2, grandTotal: 68,
          payouts: [{ paymentModeId: 'm-cash', paymentModeName: 'Cash', kind: 'Cash', amount: 68 }],
          paidOut: 68, toAccount: 0, cashRefunds: 68,
        },
        netSales: 1130,
      },
    }, 'u1', [refund]);

    expect(p.text()).toContain('Refunds 1');
    expect(p.text()).toContain('Total refunded 68.00');
    expect(p.text()).toContain('Paid back: Cash 68.00');
    expect(p.text()).toContain('Net sales 1,130.00');
    expect(p.text()).toContain('Cash refunds 68.00');
    expect(p.text()).toContain('CN0001');
    expect(p.text()).toContain('Damaged can');
  });

  it('reprints a refund’s credit note as a counted copy', () => {
    const p = page(session(), 'u1', [refund]);

    const button = [...p.element.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('CN0001'))!;
    button.click();
    TestBed.tick();

    expect(p.text()).toContain('CN0001 printed as copy 1 (printed 2 times).');
    expect(window.print).toHaveBeenCalled();
  });

  it('offers a refund of each sale to its own cashier, and to nobody reading someone else’s session', () => {
    const own = page(session());
    const link = own.element.querySelector<HTMLAnchorElement>('a[href*="/pos/refund/loc-1"][href*="invoiceId=inv-1"]');
    expect(link?.textContent).toContain('Refund');
    TestBed.resetTestingModule();

    const other = page(session(), 'someone-else');
    expect(other.element.querySelector('a[href*="/pos/refund/"]')).toBeNull();
  });

  it('shows another cashier’s session read-only', () => {
    const p = page(session(), 'someone-else');

    expect(p.text()).toContain('X Report (so far)');
    expect(p.element.querySelector('#pos-movement-amount')).toBeNull();
    expect(p.element.querySelector('#pos-close-amount')).toBeNull();
  });

  it('shows a closed session as its Z report', () => {
    const p = page(session({ status: 'Closed', closedAt: '2026-10-01T12:00:00Z', countedCash: 1824, cashDifference: -9, closingNote: 'Coins short' }));

    expect(p.text()).toContain('Z Report');
    expect(p.text()).toContain('Over / (short) -9.00');
    expect(p.text()).toContain('Coins short');
  });

  it('reprints a sale as a counted copy', () => {
    const p = page(session());

    p.press('Reprint');

    expect(p.text()).toContain('INV0001 printed as copy 1 (printed 2 times).');
    expect(window.print).toHaveBeenCalled();
    expect(p.text()).toContain('2 time(s)');
  });
});
