import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import {
  CreatePosSaleRequest,
  CreatePosSaleResult,
  PosProduct,
  PosReceipt,
  PosSession,
  PosTill,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { PosTillPage } from './pos-till-page';

/**
 * Phase 62 -- the Retail till. On phase 59's numbers: Momo 200 with 10% service charge, Coke 60
 * without, both 13% VAT, rounding on. The assertions are what reaches `POST /pos/sales` -- tenders,
 * change, customer, overrides -- and what the screen refuses before it gets there.
 */
describe('PosTillPage', () => {
  const organizationId = 'org-1';
  const locationId = 'loc-1';

  const momo: PosProduct = {
    id: 'momo', code: 'P0001', name: 'Chicken Momo', categoryId: 'food', type: 'Service', vatRate: 'ThirteenPercentVat',
    rate: 200, serviceChargeApplicable: true, batchTracking: false, serialTracking: false, barcode: null,
    units: [{ unitId: 'plate', shortName: 'plt', conversionRate: 1, rate: 200 }],
  };

  const coke: PosProduct = {
    id: 'coke', code: 'P0002', name: 'Coke 250ml', categoryId: 'drinks', type: 'Goods', vatRate: 'ThirteenPercentVat',
    rate: 60, serviceChargeApplicable: false, batchTracking: false, serialTracking: false, barcode: '8901',
    units: [{ unitId: 'pc', shortName: 'pc', conversionRate: 1, rate: 60 }],
  };

  const till = (overrides: Partial<PosTill> = {}): PosTill => ({
    locationId, locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail', availableTabs: ['Retail', 'Delivery'],
    defaultTab: 'Retail', serviceChargeEnabled: true, serviceChargeRate: 10, roundOffEnabled: true,
    cashVerificationRequired: false, denominations: [1000, 500, 100], printInvoice: false,
    abbreviatedTaxInvoiceEnabled: false, isVatRegistered: true, warehouseId: 'wh', warehouseName: 'Main',
    walkInCustomer: { id: 'walk-in', code: 'WALKIN', name: 'Cash Customer' },
    paymentModes: [
      { id: 'cash', name: 'Cash', kind: 'Cash' },
      { id: 'card', name: 'Card', kind: 'Card' },
    ],
    categories: [{ id: 'food', name: 'Food' }, { id: 'drinks', name: 'Drinks' }],
    canSellOnCredit: true,
    printCreditNote: true,
    canRefund: true,
    ...overrides,
  });

  const session: PosSession = {
    id: 'ses-1', code: 'SES0001', locationId, locationName: 'Thamel', userId: 'u1', userName: 'Sita', status: 'Open',
    openedAt: '2026-10-01T03:00:00Z', closedAt: null, cashAccountId: 'cash-acct', openingFloat: 1000, openingCount: null,
    sales: {
      salesCount: 0, subTotal: 0, serviceCharge: 0, vat: 0, roundOff: 0, grandTotal: 0, tenders: [], tendered: 0,
      change: 0, settled: 0, credit: 0, cashSales: 0,
      refunds: { refundsCount: 0, subTotal: 0, serviceCharge: 0, vat: 0, roundOff: 0, grandTotal: 0, payouts: [], paidOut: 0, toAccount: 0, cashRefunds: 0, taxable: 0, nonTaxable: 0 }, netSales: 0,
      taxable: 0, nonTaxable: 0, netRoundOff: 0, netCash: 0, netCredit: 0,
    },
    cashMovements: [], cashIn: 0, cashOut: 0, expectedCash: 1000, countedCash: null, closingCount: null,
    cashDifference: null, closingNote: null,
  };

  class PosServiceStub {
    sent: CreatePosSaleRequest[] = [];
    printed: string[] = [];
    codeLookup: PosProduct[] = [];
    failNext: unknown = null;

    constructor(
      private readonly current: PosTill,
      private readonly mine: PosSession | null,
    ) {}

    getTill(): Observable<PosTill> {
      return of(this.current);
    }

    getMySession(): Observable<PosSession | null> {
      return of(this.mine);
    }

    listProducts(_o: string, _l: string, query: { code?: string; search?: string }): Observable<PagedResult<PosProduct>> {
      const items = query.code ? this.codeLookup : query.search ? [] : [momo, coke];
      return of({ items, page: 1, pageSize: 48, totalCount: items.length });
    }

    createSale(_o: string, request: CreatePosSaleRequest): Observable<CreatePosSaleResult> {
      this.sent.push(request);
      if (this.failNext) {
        const failure = this.failNext;
        this.failNext = null;
        return throwError(() => failure);
      }
      const total = 633;
      return of({
        id: 'inv-1', code: 'INV0001', grandTotal: total, serviceCharge: 40, roundOff: 0.2,
        tendered: request.tenders.reduce((s, t) => s + t.amount, 0), changeAmount: request.changeAmount,
        creditAmount: 0, isAbbreviatedTaxInvoice: false,
      });
    }

    printReceipt(_o: string, invoiceId: string): Observable<PosReceipt> {
      this.printed.push(invoiceId);
      return of(receipt(this.printed.length));
    }
  }

  const receipt = (printNumber: number): PosReceipt => ({
    invoiceId: 'inv-1', code: 'INV0001', title: 'TaxInvoice', printNumber, printedAt: '2026-10-01T05:00:00Z',
    printedByName: 'Sita', sellerName: 'Momo Ghar', sellerAddress: 'Thamel', sellerPan: '601234567',
    locationName: 'Thamel', date: '2026-10-01', soldAt: '2026-10-01T05:00:00Z', sessionCode: 'SES0001',
    cashierName: 'Sita', orderType: 'Retail', customerName: 'Cash Customer', customerAddress: null, customerPan: null,
    isWalkIn: true, lines: [], grossAmount: 520, discountAmount: 0, subTotal: 520, serviceCharge: 40,
    taxableAmount: 560, nonTaxableAmount: 0, vat: 72.8, roundOff: 0.2, grandTotal: 633,
    amountInWords: 'Rupees Six Hundred Thirty Three Only', tenders: [], tendered: 1000, changeAmount: 367,
    creditAmount: 0,
  });

  const acme: Contact = {
    id: 'acme', organizationId, type: 'Customer', name: 'Acme Retail', code: 'C0001', address: null,
  } as Contact;

  beforeEach(() => {
    localStorage.clear();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  function page(current: PosTill = till(), mine: PosSession | null = session) {
    const service = new PosServiceStub(current, mine);

    TestBed.configureTestingModule({
      imports: [PosTillPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosService, useValue: service },
        { provide: ContactsService, useValue: { listContacts: () => of({ items: [acme], page: 1, pageSize: 8, totalCount: 1 }) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ id: organizationId, locationId }) },
            paramMap: of(convertToParamMap({ id: organizationId, locationId })),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosTillPage);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    const buttons = (label: string) =>
      [...element.querySelectorAll<HTMLButtonElement>('button')].filter((b) => b.textContent?.trim().startsWith(label));
    const press = (label: string) => {
      buttons(label)[0].click();
      render();
    };
    const type = (id: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      render();
    };
    const tile = (name: string) => {
      [...element.querySelectorAll<HTMLButtonElement>('.pos-tile')].find((b) => b.textContent?.includes(name))!.click();
      render();
    };
    const chooseMode = (id: string) => {
      element.querySelector<HTMLInputElement>(`#pos-mode-${id}`)!.click();
      render();
    };
    const tender = (modeId: string, amount: string) => {
      chooseMode(modeId);
      type('pos-tender-amount', amount);
      press('Add');
    };

    return {
      fixture, service, element, render, buttons, press, type, tile, tender,
      // Text nodes joined by a space, so adjacent cells (a <dt> and its <dd>) read as two words.
      text: () => {
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        const parts: string[] = [];
        for (let node = walker.nextNode(); node; node = walker.nextNode()) parts.push(node.textContent ?? '');
        return parts.join(' ').replace(/\s+/g, ' ');
      },
      complete: () => buttons('Complete Sale')[0],
    };
  }

  it('offers Refund only to a cashier who may refund here (phase 63)', () => {
    const can = page(till({ canRefund: true }));
    expect(can.element.querySelector('a[href*="/pos/refund/"]')?.textContent?.trim()).toBe('Refund');
    TestBed.resetTestingModule();

    const cannot = page(till({ canRefund: false }));
    expect(cannot.element.querySelector('a[href*="/pos/refund/"]')).toBeNull();
  });

  it('cannot sell without a session of the cashier’s own, and says where to start one', () => {
    const p = page(till(), null);

    expect(p.text()).toContain('You have no session open at Thamel');
    expect(p.element.querySelector('.pos-tile')).toBeNull();
  });

  it("totals phase 59's table on screen exactly as the server will bill it", () => {
    const p = page();

    p.tile('Chicken Momo');
    p.tile('Chicken Momo');
    p.tile('Coke 250ml');
    p.tile('Coke 250ml');

    expect(p.text()).toContain('Service Charge (10%) 40.00');
    expect(p.text()).toContain('Round Off 0.20');
    expect(p.text()).toContain('Total 633.00');
  });

  it('takes a cash sale with change, sends the change, and lands on the next customer', () => {
    const p = page();
    p.tile('Chicken Momo');
    p.tile('Chicken Momo');
    p.tile('Coke 250ml');
    p.tile('Coke 250ml');

    p.press('Pay (F9)');
    p.tender('cash', '1000');

    expect(p.text()).toContain('Change 367.00');
    p.complete().click();
    p.render();

    expect(p.service.sent[0]).toEqual(expect.objectContaining({
      sessionId: 'ses-1',
      locationId,
      tenders: [{ paymentModeId: 'cash', amount: 1000 }],
      changeAmount: 367,
      contactId: null,
      orderType: 'Retail',
      discountPct: 0,
      overrideStockWarning: false,
      overrideCreditLimitWarning: false,
    }));
    expect(p.service.sent[0].lines.map((x) => [x.productId, x.quantity, x.rate, x.unitId])).toEqual([
      ['momo', 2, 200, null],
      ['coke', 2, 60, null],
    ]);
    expect(p.text()).toContain('Sale INV0001 complete');
    expect(p.text()).toContain('Change 367.00');
    // The location does not auto-print, so nothing was counted as a printing.
    expect(p.service.printed).toEqual([]);
  });

  it('splits a bill across card and cash', () => {
    const p = page();
    p.tile('Chicken Momo');
    p.tile('Coke 250ml');

    p.press('Pay (F9)');
    p.tender('card', '16');
    p.tender('cash', '300');
    p.complete().click();
    p.render();

    expect(p.service.sent[0].tenders).toEqual([
      { paymentModeId: 'card', amount: 16 },
      { paymentModeId: 'cash', amount: 300 },
    ]);
    expect(p.service.sent[0].changeAmount).toBe(0);
  });

  it('refuses a card taken for more than the bill, because only cash gives change', () => {
    const p = page();
    p.tile('Coke 250ml');

    p.press('Pay (F9)');
    p.tender('card', '100');

    expect(p.text()).toContain('Only cash gives change');
    expect(p.complete().disabled).toBe(true);
  });

  it('never leaves the walk-in owing, and says so before Pay', () => {
    const p = page();
    p.tile('Chicken Momo');

    p.press('Pay (F9)');
    p.tender('card', '100');

    expect(p.text()).toContain('The walk-in customer cannot take credit');
    expect(p.complete().disabled).toBe(true);
    expect(p.service.sent).toEqual([]);
  });

  it('leaves the rest on a named customer’s account once the cashier confirms it', () => {
    const p = page();
    p.tile('Chicken Momo');

    p.press('Change Customer');
    p.type('pos-customer-search', 'Acme');
    return new Promise<void>((resolve) => setTimeout(resolve, 300)).then(() => {
      p.render();
      p.press('Acme Retail');

      p.press('Pay (F9)');
      p.tender('card', '100');
      expect(p.complete().disabled).toBe(true);

      p.element.querySelector<HTMLInputElement>('#pos-credit-accept')!.click();
      p.render();
      p.complete().click();
      p.render();

      expect(p.service.sent[0]).toEqual(expect.objectContaining({
        contactId: 'acme', tenders: [{ paymentModeId: 'card', amount: 100 }], changeAmount: 0,
      }));
    });
  });

  it('names the missing key when the cashier may not leave anything on credit', () => {
    const p = page(till({ canSellOnCredit: false }));
    p.tile('Chicken Momo');
    p.press('Change Customer');
    p.type('pos-customer-search', 'Acme');
    return new Promise<void>((resolve) => setTimeout(resolve, 300)).then(() => {
      p.render();
      p.press('Acme Retail');
      p.press('Pay (F9)');
      p.tender('card', '100');

      expect(p.text()).toContain('Sales.Invoice.Approve');
      expect(p.element.querySelector('#pos-credit-accept')).toBeNull();
    });
  });

  it('confirms a stock warning and sends the sale again with only that override', () => {
    const p = page();
    p.tile('Coke 250ml');
    p.service.failNext = new HttpErrorResponse({
      status: 422,
      error: { title: 'Coke 250ml has 0 in stock.', warningKind: 'StockAvailability' },
    });

    p.press('Pay (F9)');
    p.tender('cash', '68');
    p.complete().click();
    p.render();

    expect(p.text()).toContain('Coke 250ml has 0 in stock.');
    p.press('Sell Anyway');

    expect(p.service.sent.length).toBe(2);
    expect(p.service.sent[1].overrideStockWarning).toBe(true);
    expect(p.service.sent[1].overrideCreditLimitWarning).toBe(false);
  });

  it('prints the original at once where the location prints invoices, then marks a reprint as a copy', () => {
    const p = page(till({ printInvoice: true }));
    p.tile('Coke 250ml');
    p.press('Pay (F9)');
    p.tender('cash', '100');
    p.complete().click();
    p.render();

    expect(p.service.printed).toEqual(['inv-1']);
    expect(window.print).toHaveBeenCalled();
    expect(p.text()).not.toContain('COPY OF ORIGINAL');

    p.press('Print a Copy');

    expect(p.service.printed).toEqual(['inv-1', 'inv-1']);
    expect(p.text()).toContain('COPY OF ORIGINAL · printed 2 times');
  });

  it('adds a scanned code’s one product and clears the box for the next scan', () => {
    const p = page();
    p.service.codeLookup = [coke];

    p.type('pos-search', '8901');
    p.element.querySelector<HTMLFormElement>('form[role="search"]')!.dispatchEvent(new Event('submit'));
    p.render();

    expect(p.text()).toContain('Total 68.00');
    expect(p.element.querySelector<HTMLInputElement>('#pos-search')!.value).toBe('');
  });

  it('parks a cart and recalls it', () => {
    const p = page();
    p.tile('Chicken Momo');

    p.press('Hold');
    expect(p.text()).toContain('The cart is empty');
    expect(p.text()).toContain('Held carts (1)');

    p.press('Held carts (1)');
    p.press('Recall');
    expect(p.text()).toContain('Chicken Momo');
    expect(p.text()).toContain('Held carts (0)');
  });

  it('opens payment on F9 and gives focus back to Pay on Escape', () => {
    const p = page();
    p.tile('Coke 250ml');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9' }));
    p.render();
    const dialog = p.element.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(document.activeElement?.id).toBe('pos-tender-amount');

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    p.render();
    expect(p.element.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement?.textContent?.trim()).toBe('Pay (F9)');
  });
});
