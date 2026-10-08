import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PosReceipt, PosTill } from '../../../core/pos/pos.models';
import {
  CreatePosOrderInvoiceRequest,
  CreatePosOrderInvoiceResult,
  PosOrder,
  PosOrderBillPreview,
  PosOrderBillRequest,
  PosRestaurant,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosService } from '../../../core/pos/pos.service';
import { PosBillPage } from './pos-bill-page';

/**
 * Phase 65 -- the bill screen on phase 59's table (2 Momo + 2 Coke, 633): every figure it shows comes
 * from the server's preview, and paying sends the part it previewed.
 */
describe('PosBillPage', () => {
  const organizationId = 'org-1';
  const locationId = 'loc-1';

  const restaurant = (overrides: Partial<PosRestaurant> = {}): PosRestaurant => ({
    locationId, locationCode: '1002', locationName: 'POS Restaurant', availableTabs: ['DineIn', 'TakeAway', 'Delivery'],
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, printKot: false, canvasWidth: 1100,
    canvasHeight: 800, walkInCustomer: null, categories: [], areas: [], orders: [], canVoid: true,
    roundOffEnabled: true, printEstimateBill: true, printInvoice: false, mySessionId: 'ses-1', mySessionCode: 'SES0001',
    canKitchen: true, serviceChargeOnTakeAway: true,
    ...overrides,
  });

  const till: PosTill = {
    locationId, locationCode: '1002', locationName: 'POS Restaurant', posMode: 'Restaurant', availableTabs: ['DineIn'],
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, roundOffEnabled: true,
    cashVerificationRequired: false, denominations: [], printInvoice: false, abbreviatedTaxInvoiceEnabled: false,
    isVatRegistered: true, warehouseId: 'wh', warehouseName: 'Main', walkInCustomer: null,
    paymentModes: [{ id: 'cash', name: 'Cash', kind: 'Cash' }, { id: 'card', name: 'Card', kind: 'Card' }],
    categories: [], canSellOnCredit: false, printCreditNote: false, canRefund: false,
  } as PosTill;

  const line = (id: string, name: string, quantity: number) => ({
    id, lineNo: 1, productId: id, productCode: 'P', productName: name, unitId: null, unitName: null, rate: 200,
    vatRate: 'ThirteenPercentVat' as const, serviceChargeRate: 10, note: null, kitchenStationId: null,
    kitchenStationName: 'Default', ordered: quantity, discarded: 0, quantity, served: 0, outstanding: quantity,
    invoiced: 0, toBill: quantity, amount: 0, serviceChargeAmount: 0, vatAmount: 0, total: 0, isTakeAway: false, parcelledFromLineId: null, movedIn: 0, movedOut: 0,
  });

  const order = (overrides: Partial<PosOrder> = {}): PosOrder => ({
    id: 'ord-1', code: 'ORD0001', locationId, locationCode: '1002', locationName: 'POS Restaurant', orderType: 'DineIn',
    status: 'Open', tableId: 't1', tableName: 'T1', areaId: 'gf', areaName: 'Ground Floor', covers: 2, contactId: null,
    contactName: 'Cash Customer', date: '2026-10-02', createdAt: '2026-10-02T04:00:00Z', createdByName: 'Sita',
    voidReason: null, voidedAt: null, voidedByName: null, amount: 520, serviceCharge: 40, vat: 72.8, total: 632.8,
    outstanding: 4, settledAt: null, billed: 0, toBill: 4, invoices: [], tickets: [],
    lines: [line('momo', 'Chicken Momo', 2), line('coke', 'Coke 250ml', 2)],
    ...overrides,
  });

  /** The whole table (632.80 to 633), a first half (316.40 to 316), or the rest after it (316.40 to 317). */
  const preview = (part: 'whole' | 'first' | 'rest'): PosOrderBillPreview => {
    const unrounded = part === 'whole' ? 632.8 : 316.4;
    const total = part === 'whole' ? 633 : part === 'first' ? 316 : 317;
    return {
      orderId: 'ord-1', orderCode: 'ORD0001', lines: [], amount: unrounded === 632.8 ? 520 : 260,
      serviceCharge: part === 'whole' ? 40 : 20, vat: part === 'whole' ? 72.8 : 36.4, unrounded,
      roundOff: Math.round((total - unrounded) * 100) / 100, total, billsTheRest: part !== 'first', orderTotal: 633,
      billedBefore: part === 'rest' ? 316 : 0, leftAfter: part === 'first' ? 317 : 0,
    };
  };

  function page(options: { restaurant?: PosRestaurant } = {}) {
    vi.useFakeTimers();
    const previews: PosOrderBillRequest[] = [];
    const bills: CreatePosOrderInvoiceRequest[] = [];
    let paid = false;

    const restaurantService = {
      getOrder: (): Observable<PosOrder> => of(paid ? order({ billed: 316, toBill: 2 }) : order()),
      getRestaurant: (): Observable<PosRestaurant> => of(options.restaurant ?? restaurant()),
      previewBill: (_o: string, _id: string, request: PosOrderBillRequest): Observable<PosOrderBillPreview> => {
        previews.push(request);
        return of(paid ? preview('rest') : request.split === 'Whole' ? preview('whole') : preview('first'));
      },
      billOrder: (_o: string, _id: string, request: CreatePosOrderInvoiceRequest): Observable<CreatePosOrderInvoiceResult> => {
        bills.push(request);
        paid = true;
        return of({
          id: 'inv-1', code: 'INV0001', grandTotal: 316, serviceCharge: 20, roundOff: -0.4, tendered: 500, changeAmount: 184,
          creditAmount: 0, isAbbreviatedTaxInvoice: false, orderStatus: 'Open', orderToBill: 317,
        });
      },
    };
    const posService = {
      getTill: (): Observable<PosTill> => of(till),
      printReceipt: (): Observable<PosReceipt> => of({} as PosReceipt),
    };

    TestBed.configureTestingModule({
      imports: [PosBillPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosRestaurantService, useValue: restaurantService },
        { provide: PosService, useValue: posService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId, orderId: 'ord-1' }) } },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosBillPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      vi.advanceTimersByTime(300);
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    return {
      element, previews, bills, render,
      text: () => element.textContent?.replace(/\s+/g, ' ') ?? '',
      click: (selector: string) => {
        element.querySelector<HTMLElement>(selector)!.click();
        render();
      },
      change: (selector: string, value: string | boolean) => {
        const input = element.querySelector<HTMLInputElement>(selector)!;
        if (typeof value === 'boolean') input.checked = value;
        else input.value = value;
        input.dispatchEvent(new Event(typeof value === 'boolean' ? 'change' : 'input'));
        input.dispatchEvent(new Event('change'));
        render();
      },
    };
  }

  afterEach(() => vi.useRealTimers());

  it('prices the whole order on the server before anything is chosen', () => {
    const p = page();

    expect(p.previews[0]).toEqual({ split: 'Whole', items: [], parts: null });
    expect(p.text()).toContain('Round off0.20This bill633.00');
    expect(p.text()).toContain('Service charge40.00');
    expect(p.text()).toContain('This bill settles the order.');
    expect(p.text()).toContain('Due 633.00');
  });

  it('asks for one of N equal parts, defaulting N to the guests', () => {
    const p = page();

    p.click('#pos-bill-split-equal');

    expect(p.previews.at(-1)).toEqual({ split: 'Equal', items: [], parts: 2 });
    expect(p.text()).toContain('317.00 will be left for later bills.');
  });

  it('bills the ticked items in their chosen quantities, and counts an equal split down after a part is paid', () => {
    const p = page();

    p.click('#pos-bill-split-items');
    expect(p.text()).toContain('Tick what goes on this bill.');
    p.change('#pos-bill-item-momo', true);
    p.change('#pos-bill-qty-coke', '1');

    expect(p.previews.at(-1)).toEqual({
      split: 'Items', items: [{ lineId: 'momo', quantity: 2 }, { lineId: 'coke', quantity: 1 }], parts: null,
    });

    // The amount field is prefilled with what is due; Add takes it, then Take Payment.
    p.click('#pos-bill-mode-cash');
    p.change('#pos-bill-amount', '500');
    [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === 'Add')!.click();
    p.render();
    [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Take Payment'))!.click();
    p.render();

    expect(p.bills[0]).toMatchObject({
      split: 'Items', sessionId: 'ses-1', locationId, tenders: [{ paymentModeId: 'cash', amount: 500 }], changeAmount: 184,
    });
    expect(p.text()).toContain('Bill INV0001 paid: 316.00');
    expect(p.text()).toContain('Change 184.00');
    // The Pay button went with the bill it paid; focus is on what happened.
    expect(document.activeElement?.id).toBe('pos-bill-done');
  });

  it('says a waiter without a drawer cannot take payment, before anything is chosen', () => {
    const p = page({ restaurant: restaurant({ mySessionId: null, mySessionCode: null }) });

    expect(p.text()).toContain('Taking payment needs your own open session at this till');
    expect(p.element.querySelector('#pos-bill-split-whole')).toBeNull();
  });
});
