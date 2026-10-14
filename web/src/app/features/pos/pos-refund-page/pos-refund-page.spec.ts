import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import {
  CreatePosRefundRequest,
  CreatePosRefundResult,
  PosRefundPreview,
  PosRefundReceipt,
  PosRefundableSale,
  PosSaleMatch,
  PosSession,
  PosTill,
  PreviewPosRefundRequest,
} from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { PosRefundPage } from './pos-refund-page';

/**
 * Phase 63 -- the refund screen on phase 59's numbers: a walk-in's bill of 2 momo and 2 coke, of which
 * one coke (60 + 7.80 VAT, rounded to 68) comes back in cash; and a named customer's credit sale,
 * whose refund comes off their account with nothing handed back.
 */
describe('PosRefundPage', () => {
  const organizationId = 'org-1';
  const locationId = 'loc-1';

  const till = (overrides: Partial<PosTill> = {}): PosTill => ({
    locationId, locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail', availableTabs: ['Retail'],
    defaultTab: 'Retail', serviceChargeEnabled: true, serviceChargeRate: 10, packagingFeeBasis: 'None', packagingFeeValue: 0, roundOffEnabled: true,
    cashVerificationRequired: false, denominations: [], printInvoice: true, abbreviatedTaxInvoiceEnabled: false,
    isVatRegistered: true, warehouseId: 'wh', warehouseName: 'Main', walkInCustomer: null,
    paymentModes: [
      { id: 'm-card', name: 'Card', kind: 'Card' },
      { id: 'm-cash', name: 'Cash', kind: 'Cash' },
    ],
    categories: [], canSellOnCredit: true, printCreditNote: true, canRefund: true, canDiscount: true,
    canChangeRate: true,
    ...overrides,
  });

  const session = { id: 'ses-1', code: 'SES0002', locationId } as unknown as PosSession;

  const match: PosSaleMatch = {
    invoiceId: 'inv-1', code: '0001', date: '2026-10-01', soldAt: '2026-10-01T04:00:00Z', sessionCode: 'SES0001',
    customerName: 'Cash Customer', isWalkIn: true, grandTotal: 633, soldAtLocationName: null, fullyRefunded: false,
  };

  const sale = (overrides: Partial<PosRefundableSale> = {}): PosRefundableSale => ({
    invoiceId: 'inv-1', code: '0001', date: '2026-10-01', soldAt: '2026-10-01T04:00:00Z', locationId,
    sessionCode: 'SES0001', contactId: 'walk-in', customerName: 'Cash Customer', isWalkIn: true, grandTotal: 633,
    settled: 633, owed: 0,
    lines: [
      { invoiceLineId: 'l-momo', productId: 'momo', productName: 'Chicken Momo', unitShortName: 'pc', sold: 2, remaining: 2,
        rate: 200, discountPct: 0, serviceChargeRate: 10, packagingFeeAmount: 0, vatRate: 'ThirteenPercentVat', lineTotal: 497.2, discountPerUnit: 0 },
      { invoiceLineId: 'l-coke', productId: 'coke', productName: 'Coke 250ml', unitShortName: 'pc', sold: 2, remaining: 2,
        rate: 60, discountPct: 0, serviceChargeRate: 0, packagingFeeAmount: 0, vatRate: 'ThirteenPercentVat', lineTotal: 135.6, discountPerUnit: 0 },
    ],
    priorRefunds: [], canRefund: true, soldAtLocationName: null, returnWarehouseName: 'Main',
    ...overrides,
  });

  const cokePreview: PosRefundPreview = {
    lines: [{ invoiceLineId: 'l-coke', quantity: 1, amount: 60, serviceChargeAmount: 0, packagingFeeAmount: 0, vatAmount: 7.8, lineTotal: 67.8 }],
    subTotal: 60, serviceCharge: 0, packagingFee: 0, vat: 7.8, roundOff: 0.2, grandTotal: 68, owedBefore: 0, requiredPayout: 68, toAccount: 0,
  };

  function page(options: {
    till?: PosTill; session?: PosSession | null; sale?: PosRefundableSale; preview?: PosRefundPreview; invoiceId?: string;
    matches?: PosSaleMatch[];
  } = {}) {
    const searched: string[] = [];
    const previews: PreviewPosRefundRequest[] = [];
    const created: CreatePosRefundRequest[] = [];
    const printed: string[] = [];
    const read: string[] = [];
    const service = {
      getTill: (): Observable<PosTill> => of(options.till ?? till()),
      getMySession: (): Observable<PosSession | null> => of(options.session === undefined ? session : options.session),
      findSales: (_o: string, _l: string, search: string): Observable<PosSaleMatch[]> => {
        searched.push(search);
        return of(options.matches ?? [match]);
      },
      getRefundableSale: (_o: string, l: string, invoiceId: string): Observable<PosRefundableSale> => {
        read.push(`${l}/${invoiceId}`);
        return of(options.sale ?? sale());
      },
      previewRefund: (_o: string, request: PreviewPosRefundRequest): Observable<PosRefundPreview> => {
        previews.push(request);
        return of(options.preview ?? cokePreview);
      },
      createRefund: (_o: string, request: CreatePosRefundRequest): Observable<CreatePosRefundResult> => {
        created.push(request);
        const p = options.preview ?? cokePreview;
        return of({ id: 'cn-1', code: 'CN0001', grandTotal: p.grandTotal, serviceCharge: p.serviceCharge, packagingFee: 0,
          roundOff: p.roundOff, paidOut: p.requiredPayout, toAccount: p.toAccount });
      },
      printRefundReceipt: (_o: string, id: string): Observable<PosRefundReceipt> => {
        printed.push(id);
        return of({ code: 'CN0001', printNumber: 1, lines: [], payouts: [] } as unknown as PosRefundReceipt);
      },
    };

    TestBed.configureTestingModule({
      imports: [PosRefundPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosService, useValue: service },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ id: organizationId, locationId }),
              queryParamMap: convertToParamMap(options.invoiceId ? { invoiceId: options.invoiceId } : {}),
            },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosRefundPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    return {
      element,
      searched,
      previews,
      created,
      printed,
      read,
      render,
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
      quantity: (lineId: string, value: number) => {
        const input = element.querySelector<HTMLInputElement>(`#pos-refund-qty-${lineId}`)!;
        input.value = String(value);
        input.dispatchEvent(new Event('change'));
        render();
      },
      type: (id: string, value: string) => {
        const input = element.querySelector<HTMLInputElement | HTMLTextAreaElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        render();
      },
      submit: () => {
        element.querySelector('[aria-labelledby="pos-refund-pay-heading"] form')!.dispatchEvent(new Event('submit'));
        render();
      },
    };
  }

  beforeEach(() => {
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  it('lists the till’s recent sales to choose from and searches by bill number', () => {
    const p = page();

    expect(p.searched).toEqual(['']);
    expect(p.text()).toContain('0001');
    expect(p.element.querySelector('#pos-refund-search')).toBe(document.activeElement);

    p.type('pos-refund-search', '0001');
    p.element.querySelector('form[role="search"]')!.dispatchEvent(new Event('submit'));
    expect(p.searched).toEqual(['', '0001']);
  });

  it('refunds one coke in cash: the figure and the payout are the server’s, and the reason is required', () => {
    const p = page();
    p.press('Refund');

    expect(p.text()).toContain('Bill 0001');
    p.quantity('l-coke', 1);

    expect(p.previews).toEqual([{ sessionId: 'ses-1', invoiceId: 'inv-1', lines: [{ invoiceLineId: 'l-coke', quantity: 1 }] }]);
    expect(p.text()).toContain('Refund total 68.00');
    expect(p.text()).toContain('Round off 0.20');
    expect(p.text()).toContain('Hand back 68.00 in');
    // Cash is chosen first, wherever it sits in the till's list.
    expect(p.element.querySelector<HTMLInputElement>('#pos-refund-mode-m-cash')!.checked).toBe(true);

    p.submit();
    expect(p.created).toEqual([]);
    const reason = p.element.querySelector('#pos-refund-reason')!;
    expect(reason.getAttribute('aria-invalid')).toBe('true');
    expect(reason.getAttribute('aria-describedby')).toBe('pos-refund-reason-error');

    p.type('pos-refund-reason', 'Damaged can');
    p.submit();

    expect(p.created).toEqual([{
      sessionId: 'ses-1', locationId, invoiceId: 'inv-1', lines: [{ invoiceLineId: 'l-coke', quantity: 1 }],
      payouts: [{ paymentModeId: 'm-cash', amount: 68 }], reason: 'Damaged can',
    }]);
    expect(p.text()).toContain('Refund CN0001 complete: 68.00, handed back 68.00 in Cash.');
    // Phase 84 -- the sale is read under this till.
    expect(p.read[0]).toBe(`${locationId}/inv-1`);
    // The location prints credit notes, so the first print is made at once.
    expect(p.printed).toEqual(['cn-1']);
    expect(window.print).toHaveBeenCalled();
  });

  it('never asks more of a line than is left to refund', () => {
    const p = page();
    p.press('Refund');

    p.quantity('l-coke', 5);

    expect(p.previews.at(-1)!.lines).toEqual([{ invoiceLineId: 'l-coke', quantity: 2 }]);
  });

  it('a credit sale’s refund comes off the customer’s account and hands nothing back', () => {
    const p = page({
      invoiceId: 'inv-1',
      sale: sale({ customerName: 'Acme Retail', isWalkIn: false, settled: 0, owed: 249 }),
      preview: { ...cokePreview, grandTotal: 249, owedBefore: 249, requiredPayout: 0, toAccount: 249 },
    });

    // Arriving from the session page with the sale named skips the search.
    expect(p.text()).toContain('Bill 0001');
    expect(p.text()).toContain('still owed 249.00');

    p.quantity('l-momo', 1);
    expect(p.text()).toContain('Nothing is handed back');
    expect(p.element.querySelector('#pos-refund-mode-m-cash')).toBeNull();

    p.type('pos-refund-reason', 'Wrong order');
    p.submit();
    expect(p.created[0].payouts).toEqual([]);
  });

  it('says what a cashier without the refund keys is missing, before anything is chosen', () => {
    const p = page({ till: till({ canRefund: false }) });

    expect(p.text()).toContain('Sales.CreditNote.Create and Sales.CreditNote.Approve');
    expect(p.element.querySelector('#pos-refund-search')).toBeNull();
    expect(p.searched).toEqual([]);
  });

  // ---- Phase 84 ----------------------------------------------------------------------------------

  const twoCokes: PosRefundPreview = {
    ...cokePreview, subTotal: 120, vat: 15.6, roundOff: 0.4, grandTotal: 136, requiredPayout: 136,
  };

  it('splits a payout across modes, a part at a time, and sends exactly the parts', () => {
    const p = page({ preview: twoCokes });
    p.press('Refund');
    p.quantity('l-coke', 2);
    p.type('pos-refund-reason', 'Flat');

    const amount = () => p.element.querySelector<HTMLInputElement>('#pos-refund-payout-amount')!;
    expect(amount().value).toBe('136.00');

    // 100 in Cash, the default mode.
    p.type('pos-refund-payout-amount', '100');
    p.press('Add part');
    expect(p.text()).toContain('Still to hand back 36.00');
    expect(amount().value).toBe('36.00');
    expect(document.activeElement).toBe(amount());
    const complete = () => [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((x) => x.textContent?.includes('Complete Refund'))!;
    expect(complete().disabled).toBe(true);

    // The rest by Card.
    p.element.querySelector<HTMLInputElement>('#pos-refund-mode-m-card')!.dispatchEvent(new Event('change'));
    p.press('Add part');
    expect(p.text()).toContain('All of it is accounted for.');
    expect(complete().disabled).toBe(false);

    p.submit();
    expect(p.created[0].payouts).toEqual([
      { paymentModeId: 'm-cash', amount: 100 },
      { paymentModeId: 'm-card', amount: 36 },
    ]);
    expect(p.text()).toContain('handed back 100.00 in Cash and 36.00 in Card.');
  });

  it('refuses a part larger than what is left, and a removed part hands focus back to the amount', () => {
    const p = page({ preview: twoCokes });
    p.press('Refund');
    p.quantity('l-coke', 2);

    p.type('pos-refund-payout-amount', '200');
    p.press('Add part');
    const amount = p.element.querySelector<HTMLInputElement>('#pos-refund-payout-amount')!;
    expect(amount.getAttribute('aria-invalid')).toBe('true');
    expect(p.text()).toContain('Only 136.00 is still to hand back.');

    p.type('pos-refund-payout-amount', '50');
    p.press('Add part');
    const remove = p.element.querySelector<HTMLButtonElement>('[aria-label="Remove Cash 50.00"]')!;
    remove.focus();
    remove.click();
    p.render();
    expect(p.element.querySelector('[aria-label="Remove Cash 50.00"]')).toBeNull();
    expect(document.activeElement).toBe(p.element.querySelector('#pos-refund-payout-amount'));
    expect(p.element.querySelector<HTMLInputElement>('#pos-refund-payout-amount')!.value).toBe('136.00');
  });

  it('names another location’s sale, restaurant bills included, and marks a fully refunded one', () => {
    const p = page({
      matches: [
        { ...match, invoiceId: 'inv-away', code: 'INV0009/1003', soldAtLocationName: 'POS Retail' },
        { ...match, invoiceId: 'inv-done', code: '0002', fullyRefunded: true },
        // Phase 85 -- a restaurant bill from another branch is refunded here like any other.
        { ...match, invoiceId: 'inv-sc', code: 'INV0004/1002', soldAtLocationName: 'POS Restaurant' },
      ],
    });

    expect(p.text()).toContain('sold at POS Retail');
    expect(p.text()).toContain('sold at POS Restaurant');
    expect(p.text()).toContain('Fully refunded');
    expect(p.text()).not.toContain('Refund it at');
    const refundButtons = [...p.element.querySelectorAll<HTMLButtonElement>('table button')];
    expect(refundButtons.map((x) => x.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Refund bill INV0009/1003',
      'Refund bill INV0004/1002',
    ]);
  });

  it('says where another location’s sale was sold and where its goods go back', () => {
    const p = page({
      invoiceId: 'inv-away',
      sale: sale({ soldAtLocationName: 'POS Retail', returnWarehouseName: 'Lakeside Store' }),
    });

    expect(p.text()).toContain('Sold at POS Retail.');
    expect(p.text()).toContain('the goods go back into Lakeside Store.');
  });

  it('sends a cashier with no open session to the till first', () => {
    const p = page({ session: null });

    expect(p.text()).toContain('Open a session at this till before refunding');
    expect(p.searched).toEqual([]);
  });
  // ---- Phase 91: a serial-tracked line is refunded by ticking its units ----

  it('ticks which units of a phone came back, sends them, and refunds everything left as every unit still out', () => {
    const phoneSale = sale({
      lines: [
        { invoiceLineId: 'l-phone', productId: 'phone', productName: 'P91 Phone', unitShortName: 'pc', sold: 3, remaining: 2,
          rate: 10000, discountPct: 0, serviceChargeRate: 0, packagingFeeAmount: 0, vatRate: 'ThirteenPercentVat', lineTotal: 33900,
          discountPerUnit: 0, serialNumbers: ['IMEI-1', 'IMEI-2', 'IMEI-3'], returnableSerialNumbers: ['IMEI-1', 'IMEI-3'] },
      ],
    });
    const p = page({ sale: phoneSale });
    p.press('Refund');

    // The units sold are named on the line, and only those still out are offered; there is no quantity box to type in.
    expect(p.text()).toContain('SN IMEI-1, IMEI-2, IMEI-3');
    expect(p.element.querySelector('#pos-refund-qty-l-phone')).toBeNull();
    const boxes = [...p.element.querySelectorAll<HTMLInputElement>('input[type="checkbox"][id^="pos-refund-serial-l-phone-"]')];
    expect(boxes.map((x) => x.labels?.[0]?.textContent?.trim())).toEqual(['IMEI-1', 'IMEI-3']);

    boxes[1].click();
    p.render();
    expect(p.text()).toContain('Serials of P91 Phone (1 ticked)');
    expect(p.previews.at(-1)!.lines).toEqual([{ invoiceLineId: 'l-phone', quantity: 1, serialNumbers: ['IMEI-3'] }]);

    // Unticking the only unit refunds nothing of the line, so no preview is asked for it.
    const asked = p.previews.length;
    boxes[1].click();
    p.render();
    expect(p.previews.length).toBe(asked);
    expect(p.text()).toContain('Serials of P91 Phone (0 ticked)');

    p.press('Refund everything left');
    expect(p.previews.at(-1)!.lines).toEqual([{ invoiceLineId: 'l-phone', quantity: 2, serialNumbers: ['IMEI-1', 'IMEI-3'] }]);
    expect(boxes.every((x) => x.checked)).toBe(true);
  });
});
