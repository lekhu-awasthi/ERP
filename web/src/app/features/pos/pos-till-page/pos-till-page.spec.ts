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
  PosProductBatch,
  PosReceipt,
  PosSession,
  PosTill,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { ApplicableDiscounts } from '../../../core/sales/sales.models';
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
    rate: 200, serviceChargeApplicable: true, packagingFeeApplicable: true, batchTracking: false, serialTracking: false, barcode: null,
    units: [{ unitId: 'plate', shortName: 'plt', conversionRate: 1, rate: 200 }],
  };

  const coke: PosProduct = {
    id: 'coke', code: 'P0002', name: 'Coke 250ml', categoryId: 'drinks', type: 'Goods', vatRate: 'ThirteenPercentVat',
    rate: 60, serviceChargeApplicable: false, packagingFeeApplicable: true, batchTracking: false, serialTracking: false, barcode: '8901',
    units: [{ unitId: 'pc', shortName: 'pc', conversionRate: 1, rate: 60 }],
  };

  // Phase 91 -- a serial-tracked phone, 10000 + 13% VAT = 11300, which needs no rounding.
  const phone: PosProduct = {
    id: 'phone', code: 'P0091', name: 'P91 Phone', categoryId: 'food', type: 'Goods', vatRate: 'ThirteenPercentVat',
    rate: 10000, serviceChargeApplicable: false, packagingFeeApplicable: true, batchTracking: false, serialTracking: true,
    barcode: null, units: [{ unitId: 'pc', shortName: 'pc', conversionRate: 1, rate: 10000 }],
  };

  // Phase 94 -- a batch-tracked jar, 150 + 13% VAT = 169.50.
  const honey: PosProduct = {
    id: 'honey', code: 'P0094', name: 'P94 Honey', categoryId: 'food', type: 'Goods', vatRate: 'ThirteenPercentVat',
    rate: 150, serviceChargeApplicable: false, packagingFeeApplicable: true, batchTracking: true, serialTracking: false,
    barcode: null, units: [{ unitId: 'jar', shortName: 'jar', conversionRate: 1, rate: 150 }],
  };

  const till = (overrides: Partial<PosTill> = {}): PosTill => ({
    locationId, locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail', availableTabs: ['Retail', 'Delivery'],
    defaultTab: 'Retail', serviceChargeEnabled: true, serviceChargeRate: 10, packagingFeeBasis: 'None', packagingFeeValue: 0, roundOffEnabled: true,
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
    canDiscount: true,
    canChangeRate: true,
    ...overrides,
  });

  const session: PosSession = {
    id: 'ses-1', code: 'SES0001', locationId, locationName: 'Thamel', userId: 'u1', userName: 'Sita', status: 'Open',
    openedAt: '2026-10-01T03:00:00Z', closedAt: null, cashAccountId: 'cash-acct', openingFloat: 1000, openingCount: null,
    sales: {
      salesCount: 0, subTotal: 0, serviceCharge: 0, packagingFee: 0, vat: 0, roundOff: 0, grandTotal: 0, tenders: [], tendered: 0,
      change: 0, settled: 0, credit: 0, cashSales: 0,
      refunds: { refundsCount: 0, subTotal: 0, serviceCharge: 0, packagingFee: 0, vat: 0, roundOff: 0, grandTotal: 0, payouts: [], paidOut: 0, toAccount: 0, cashRefunds: 0, taxable: 0, nonTaxable: 0 }, netSales: 0,
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
    /** Phase 91 -- extra tiles, and the phone's units in stock here. */
    gridExtra: PosProduct[] = [];
    inStock = ['IMEI-1', 'IMEI-2', 'IMEI-3'];
    serialSearches: (string | undefined)[] = [];
    /** Phase 94 -- the jar's batches in stock here, soonest-expiring first. */
    batches: PosProductBatch[] = [
      { batchNo: 'B93A', expiryDate: '2026-10-24', quantity: 2 },
      { batchNo: 'B93B', expiryDate: null, quantity: 3 },
    ];
    batchReads = 0;
    /** Phase 97 -- what the discount schemes give at this till; none unless a test says. */
    discounts: ApplicableDiscounts = { products: [], slabs: [] };

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
      const items = query.code ? this.codeLookup : query.search ? [] : [momo, coke, ...this.gridExtra];
      return of({ items, page: 1, pageSize: 48, totalCount: items.length });
    }

    listProductSerials(_o: string, _l: string, _p: string, search?: string): Observable<string[]> {
      this.serialSearches.push(search);
      return of(search ? this.inStock.filter((x) => x.toLowerCase().includes(search.toLowerCase())) : this.inStock);
    }

    getTillDiscounts(): Observable<ApplicableDiscounts> {
      return of(this.discounts);
    }

    listProductBatches(): Observable<PosProductBatch[]> {
      this.batchReads++;
      return of(this.batches);
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
        id: 'inv-1', code: 'INV0001', grandTotal: total, serviceCharge: 40, packagingFee: 0, roundOff: 0.2,
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
    locationName: 'Thamel', locationAddress: null, date: '2026-10-01', soldAt: '2026-10-01T05:00:00Z', sessionCode: 'SES0001',
    cashierName: 'Sita', orderType: 'Retail', customerName: 'Cash Customer', customerAddress: null, customerPan: null,
    isWalkIn: true, lines: [], grossAmount: 520, discountAmount: 0, subTotal: 520, serviceCharge: 40, packagingFee: 0,
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

  it('hands focus to the amount field when a tender is removed, prefilled with what is left, and sends what remains', () => {
    const p = page();
    p.tile('Chicken Momo');
    p.tile('Coke 250ml');

    p.press('Pay (F9)');
    p.tender('card', '16');
    p.tender('cash', '200');
    // As a keyboard user does: focus the button, then press it.
    const remove = p.element.querySelector<HTMLButtonElement>('[aria-label="Remove Card tender"]')!;
    remove.focus();
    expect(document.activeElement).toBe(remove);
    remove.click();
    p.render();

    // Phase 85 -- the button went with its tender; the keyboard lands where the next tender is entered.
    expect(p.element.querySelector('[aria-label="Remove Card tender"]')).toBeNull();
    const amount = p.element.querySelector<HTMLInputElement>('#pos-tender-amount')!;
    expect(document.activeElement).toBe(amount);
    expect(amount.value).toBe('116.00');
    expect(p.text()).toContain('Card 16.00 removed.');

    p.tender('cash', '116');
    p.complete().click();
    p.render();
    expect(p.service.sent[0].tenders).toEqual([
      { paymentModeId: 'cash', amount: 200 },
      { paymentModeId: 'cash', amount: 116 },
    ]);
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

  // ---- Phase 72: a discount, or a price below the catalogue, needs Sales.Invoice.Approve here -------------

  /** The cart's price and discount fields act on change, as a cashier tabbing out of them does. */
  const setField = (p: ReturnType<typeof page>, selector: string, value: string) => {
    const input = p.element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    p.render();
  };

  it('says a discount needs Sales.Invoice.TillDiscount here, for a cashier without it, and will not complete the sale', () => {
    const p = page(till({ canDiscount: false }));
    p.tile('Chicken Momo');

    setField(p, '[id^="pos-discount-"]', '10');
    expect(p.text()).toContain('At this location, a discount needs Sales.Invoice.TillDiscount.');
    expect(p.text()).not.toContain('Sales.Invoice.TillChangeRate');

    p.press('Pay (F9)');
    p.tender('cash', '1000');
    expect(p.complete().disabled).toBe(true);
    expect(p.service.sent).toEqual([]);
  });

  it('treats any price but the catalogue\'s as needing Sales.Invoice.TillChangeRate, raised as well as lowered (phase 74, 76)', () => {
    const p = page(till({ canChangeRate: false }));
    p.tile('Chicken Momo');

    setField(p, '[id^="pos-rate-"]', '190');
    expect(p.text()).toContain('a price other than the catalogue\'s needs Sales.Invoice.TillChangeRate');

    setField(p, '[id^="pos-rate-"]', '210');
    expect(p.text()).toContain('Sales.Invoice.TillChangeRate');

    // A discount is this cashier's to give: only the price is named.
    setField(p, '[id^="pos-discount-"]', '10');
    expect(p.text()).not.toContain('Sales.Invoice.TillDiscount');

    setField(p, '[id^="pos-rate-"]', '200');
    expect(p.text()).not.toContain('Sales.Invoice.TillChangeRate');
  });

  it('names both keys when a cashier holding neither discounts and reprices (phase 76)', () => {
    const p = page(till({ canDiscount: false, canChangeRate: false }));
    p.tile('Chicken Momo');

    setField(p, '[id^="pos-rate-"]', '190');
    setField(p, '[id^="pos-discount-"]', '10');
    expect(p.text()).toContain(
      'At this location, a discount needs Sales.Invoice.TillDiscount, and a price other than the catalogue\'s needs '
      + 'Sales.Invoice.TillChangeRate.');
    expect(p.text()).not.toContain('Sales.Invoice.Approve');
  });

  // ---- Phase 74: amounts ---------------------------------------------------------------------------

  const chooseKind = (p: ReturnType<typeof page>, selector: string, kind: 'Percent' | 'Amount') => {
    const select = p.element.querySelector<HTMLSelectElement>(selector)!;
    select.value = kind;
    select.dispatchEvent(new Event('change'));
    p.render();
  };

  it('takes rupees off each unit of a line and sends them as the line\'s amount discount', () => {
    const p = page();
    p.tile('Chicken Momo');
    p.tile('Chicken Momo');

    chooseKind(p, '[id^="pos-discount-kind-"]', 'Amount');
    setField(p, '[id^="pos-discount-"]', '10');

    // 2 x 190 = 380, 38 of service charge, 13% of 418 is 54.34: 472.34, rounded to 472.
    expect(p.text()).toContain('each');
    p.press('Pay (F9)');
    p.tender('cash', '472');
    p.complete().click();
    p.render();

    expect(p.service.sent[0].lines.map((x) => [x.quantity, x.rate, x.discountPct, x.discountPerUnit])).toEqual([[2, 200, 0, 10]]);
    expect(p.service.sent[0]).toEqual(expect.objectContaining({ discountPct: 0, discountAmount: 0 }));
  });

  it('takes rupees off the whole bill, and refuses more than the bill before sending it', () => {
    const p = page();
    p.tile('Coke 250ml');

    chooseKind(p, '#pos-bill-discount-kind', 'Amount');
    setField(p, '#pos-bill-discount', '60.01');
    expect(p.text()).toContain('A discount of 60.01 is more than the bill comes to.');

    // 60 less 10 is 50, 6.50 of VAT: 56.50, rounded to 57.
    setField(p, '#pos-bill-discount', '10');
    expect(p.text()).not.toContain('more than the bill');
    p.press('Pay (F9)');
    p.tender('cash', '57');
    p.complete().click();
    p.render();

    expect(p.service.sent[0]).toEqual(expect.objectContaining({ discountPct: 0, discountAmount: 10 }));
  });

  it('starts a line at its discount scheme and the bill at its slab, which a cashier without the key keeps (phase 97)', () => {
    const p = page(till({ canDiscount: false }));
    p.service.discounts = { products: [{ productId: 'momo', discountPct: 10 }], slabs: [{ minimumAmount: 300, discountPct: 5 }] };

    p.tile('Chicken Momo');
    p.tile('Chicken Momo');

    // One line, grown by the second tap although it carries the scheme's 10%; 360 reaches the 300 slab.
    expect(p.text()).not.toContain('Sales.Invoice.TillDiscount');
    p.press('Pay (F9)');
    p.tender('cash', '1000');
    p.complete().click();
    p.render();

    expect(p.service.sent[0]).toEqual(expect.objectContaining({ discountPct: 5 }));
    expect(p.service.sent[0].lines.map((x) => [x.productId, x.quantity, x.discountPct])).toEqual([['momo', 2, 10]]);
  });

  it('keeps the bill discount a cashier set, wherever the slab moves (phase 97)', () => {
    const p = page();
    p.service.discounts = { products: [], slabs: [{ minimumAmount: 100, discountPct: 5 }, { minimumAmount: 400, discountPct: 8 }] };

    p.tile('Chicken Momo');
    setField(p, '#pos-bill-discount', '2');
    p.tile('Chicken Momo');
    p.tile('Chicken Momo');

    p.press('Pay (F9)');
    p.tender('cash', '1000');
    p.complete().click();
    p.render();

    expect(p.service.sent[0]).toEqual(expect.objectContaining({ discountPct: 2 }));
  });

  it('lets a cashier who may approve invoices give a bill discount', () => {
    const p = page();
    p.tile('Chicken Momo');

    setField(p, '#pos-bill-discount', '10');

    expect(p.text()).not.toContain('Sales.Invoice.Approve');
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
  // ---- Phase 91: serial-tracked products ----

  it('opens a serial product on its picker, refuses payment until each unit is named, and sends the units ticked', () => {
    const p = page();
    p.service.gridExtra = [phone];
    p.press('All');

    p.tile('P91 Phone');

    // The tap opened the line's picker with the cursor in its box, and the units in stock here to tick.
    expect(document.activeElement?.id).toMatch(/^pos-serial-\d+$/);
    expect(p.text()).toContain('SN none named yet (0/1)');
    const boxes = [...p.element.querySelectorAll<HTMLInputElement>('input[type="checkbox"][id^="pos-serial-"]')];
    expect(boxes.map((x) => x.labels?.[0]?.textContent?.trim())).toEqual(['IMEI-1', 'IMEI-2', 'IMEI-3']);

    p.press('Pay (F9)');
    expect(p.text()).toContain('P91 Phone is serial-tracked: 0 of 1 serial number(s) named');
    expect(p.complete().disabled).toBe(true);
    p.element.querySelector<HTMLElement>('[role="dialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    p.render();

    boxes[1].click();
    p.render();
    expect(p.text()).toContain('SN IMEI-2 (1/1)');

    p.press('Pay (F9)');
    p.tender('cash', '11300');
    p.complete().click();
    p.render();

    expect(p.service.sent[0].lines).toEqual([
      expect.objectContaining({ productId: 'phone', quantity: 1, serialNumbers: ['IMEI-2'] }),
    ]);
  });

  it('adds the phone with its unit named when the scan matched a serial in stock', () => {
    const p = page();
    p.service.codeLookup = [{ ...phone, scannedSerialNo: 'IMEI-3' }];

    p.type('pos-search', 'IMEI-3');
    p.element.querySelector<HTMLFormElement>('form[role="search"]')!.dispatchEvent(new Event('submit'));
    p.render();

    expect(p.text()).toContain('SN IMEI-3 (1/1)');
    expect(p.text()).toContain('Total 11,300.00');

    // The same unit scanned again is not sold twice.
    p.type('pos-search', 'IMEI-3');
    p.element.querySelector<HTMLFormElement>('form[role="search"]')!.dispatchEvent(new Event('submit'));
    p.render();
    expect(p.text()).toContain('Serial IMEI-3 is already in the cart.');
    expect(p.text()).toContain('SN IMEI-3 (1/1)');
  });

  it('checks a typed serial against the stock here, by keyboard, before it is named, and sends the one it took', () => {
    const p = page();
    p.service.gridExtra = [phone];
    p.press('All');
    p.tile('P91 Phone');

    const box = document.activeElement as HTMLInputElement;
    expect(box.id).toMatch(/^pos-serial-\d+$/);

    box.value = 'NOPE-9';
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    p.render();
    expect(p.text()).toContain('NOPE-9 is not a serial of P91 Phone in stock at this till.');
    expect(p.text()).toContain('SN none named yet (0/1)');

    box.value = 'imei-1';
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    p.render();
    expect(p.service.serialSearches).toEqual([undefined, 'NOPE-9', 'imei-1']);
    expect(p.text()).toContain('SN IMEI-1 (1/1)');
    expect(box.value).toBe('');

    p.press('Pay (F9)');
    p.tender('cash', '11300');
    p.complete().click();
    p.render();
    expect(p.service.sent[0].lines[0].serialNumbers).toEqual(['IMEI-1']);
  });

  // ---- Phase 94: batch-tracked products ----

  it('opens a batch product on its picker, refuses payment until a batch is chosen, and sends the one chosen', () => {
    const p = page();
    p.service.gridExtra = [honey];
    p.press('All');

    p.tile('P94 Honey');

    // The tap opened the line's picker, focus on its first radio, listing what is left here.
    const radios = [...p.element.querySelectorAll<HTMLInputElement>('input[type="radio"][id^="pos-batch-"]')];
    expect(radios.length).toBe(2);
    expect(document.activeElement).toBe(radios[0]);
    expect(radios.some((x) => x.checked)).toBe(false);
    expect(radios[0].labels?.[0]?.textContent).toContain('B93A');
    expect(radios[0].labels?.[0]?.textContent).toContain('2 jar left');
    expect(radios[0].labels?.[0]?.textContent).toContain('expires');
    expect(radios[1].labels?.[0]?.textContent).not.toContain('expires');
    expect(radios[0].closest('fieldset')?.querySelector('legend')?.textContent?.trim()).toBe('Batch');
    expect(p.text()).toContain('Batch not chosen yet');

    p.press('Pay (F9)');
    expect(p.text()).toContain('P94 Honey is batch-tracked: choose the batch it is sold from.');
    expect(p.complete().disabled).toBe(true);
    p.element.querySelector<HTMLElement>('[role="dialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    p.render();

    // By keyboard: the focused group's second radio, chosen as an arrow key chooses it.
    radios[1].focus();
    expect(document.activeElement).toBe(radios[1]);
    radios[1].checked = true;
    radios[1].dispatchEvent(new Event('change', { bubbles: true }));
    p.render();
    expect(p.text()).toContain('Batch B93B');
    expect(p.text()).not.toContain('is batch-tracked: choose');
    expect(document.activeElement).toBe(radios[1]);

    p.press('Pay (F9)');
    p.tender('cash', '200');
    p.complete().click();
    p.render();

    expect(p.service.sent[0].lines).toEqual([
      expect.objectContaining({ productId: 'honey', quantity: 1, batchNo: 'B93B', serialNumbers: null }),
    ]);
  });

  it('puts a second tap after a chosen batch on a line of its own, and re-reads the batches when a line is opened', () => {
    const p = page();
    p.service.gridExtra = [honey];
    p.press('All');

    p.tile('P94 Honey');
    const first = p.element.querySelector<HTMLInputElement>('input[type="radio"][id^="pos-batch-"]')!;
    first.checked = true;
    first.dispatchEvent(new Event('change', { bubbles: true }));
    p.render();

    p.tile('P94 Honey');
    expect(p.text()).toContain('Batch B93A');
    expect(p.text()).toContain('Batch not chosen yet');
    expect(p.service.batchReads).toBe(2);
  });

  it('sells at a picked batch\'s own price without the price key, and calls no other price changed (phase 95)', () => {
    const p = page(till({ canChangeRate: false }));
    p.service.gridExtra = [honey];
    p.service.batches = [{ batchNo: 'B4', expiryDate: null, quantity: 3, rate: 170 }];
    p.press('All');
    p.tile('P94 Honey');

    const radio = p.element.querySelector<HTMLInputElement>('input[type="radio"][id^="pos-batch-"]')!;
    expect(radio.labels?.[0]?.textContent).toContain('170.00');
    radio.checked = true;
    radio.dispatchEvent(new Event('change', { bubbles: true }));
    p.render();

    expect(p.text()).not.toContain('Sales.Invoice.TillChangeRate');
    p.press('Pay (F9)');
    p.tender('cash', '200');
    p.complete().click();
    p.render();
    expect(p.service.sent[0].lines[0]).toEqual(expect.objectContaining({ batchNo: 'B4', rate: 170 }));
  });
});
