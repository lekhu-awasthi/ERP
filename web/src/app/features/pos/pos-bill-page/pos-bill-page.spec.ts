import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
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
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, packagingFeeBasis: 'None', packagingFeeValue: 0, printKot: false, canvasWidth: 1100,
    canvasHeight: 800, walkInCustomer: null, categories: [], areas: [], orders: [], canVoid: true,
    roundOffEnabled: true, printEstimateBill: true, printInvoice: false, mySessionId: 'ses-1', mySessionCode: 'SES0001',
    canKitchen: true, serviceChargeOnTakeAway: true, canDiscount: true,
    canRemoveServiceCharge: true,
    ...overrides,
  });

  const till: PosTill = {
    locationId, locationCode: '1002', locationName: 'POS Restaurant', posMode: 'Restaurant', availableTabs: ['DineIn'],
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, packagingFeeBasis: 'None', packagingFeeValue: 0, roundOffEnabled: true,
    cashVerificationRequired: false, denominations: [], printInvoice: false, abbreviatedTaxInvoiceEnabled: false,
    isVatRegistered: true, warehouseId: 'wh', warehouseName: 'Main', walkInCustomer: null,
    paymentModes: [{ id: 'cash', name: 'Cash', kind: 'Cash' }, { id: 'card', name: 'Card', kind: 'Card' }],
    categories: [], canSellOnCredit: false, printCreditNote: false, canRefund: false, canDiscount: false,
    canChangeRate: false,
  } as PosTill;

  const line = (id: string, name: string, quantity: number) => ({
    id, lineNo: 1, productId: id, productCode: 'P', productName: name, unitId: null, unitName: null, rate: 200,
    vatRate: 'ThirteenPercentVat' as const, serviceChargeRate: 10, note: null, kitchenStationId: null,
    kitchenStationName: 'Default', ordered: quantity, discarded: 0, quantity, served: 0, outstanding: quantity,
    invoiced: 0, toBill: quantity, amount: 0, serviceChargeAmount: 0, packagingFeeAmount: 0, vatAmount: 0, total: 0, isTakeAway: false, parcelledFromLineId: null, movedIn: 0, movedOut: 0,
    discountPct: 0, gross: quantity * 200, discountPerUnit: 0, orderDiscountShare: 0, partDiscount: 0,
  });

  const order = (overrides: Partial<PosOrder> = {}): PosOrder => ({
    id: 'ord-1', code: 'ORD0001', locationId, locationCode: '1002', locationName: 'POS Restaurant', orderType: 'DineIn',
    status: 'Open', tableId: 't1', tableName: 'T1', areaId: 'gf', areaName: 'Ground Floor', covers: 2, contactId: null,
    contactName: 'Cash Customer', date: '2026-10-02', createdAt: '2026-10-02T04:00:00Z', createdByName: 'Sita',
    voidReason: null, voidedAt: null, voidedByName: null, amount: 520, serviceCharge: 40, packagingFee: 0, vat: 72.8, total: 632.8,
    outstanding: 4, settledAt: null, billed: 0, toBill: 4, invoices: [], tickets: [],
    discountPct: 0, serviceChargeWaived: false, gross: 520, discount: 0, discountAmount: 0, partDiscount: 0,
    partServiceChargeWaived: 0,
    lines: [line('momo', 'Chicken Momo', 2), line('coke', 'Coke 250ml', 2)],
    ...overrides,
  });

  /** The whole table (632.80 to 633), a first half (316.40 to 316), or the rest after it (316.40 to 317). */
  const preview = (part: 'whole' | 'first' | 'rest'): PosOrderBillPreview => {
    const unrounded = part === 'whole' ? 632.8 : 316.4;
    const total = part === 'whole' ? 633 : part === 'first' ? 316 : 317;
    return {
      orderId: 'ord-1', orderCode: 'ORD0001', lines: [], amount: unrounded === 632.8 ? 520 : 260,
      serviceCharge: part === 'whole' ? 40 : 20, packagingFee: 0, vat: part === 'whole' ? 72.8 : 36.4, unrounded,
      roundOff: Math.round((total - unrounded) * 100) / 100, total, billsTheRest: part !== 'first', orderTotal: 633,
      billedBefore: part === 'rest' ? 316 : 0, leftAfter: part === 'first' ? 317 : 0,
      gross: unrounded === 632.8 ? 520 : 260, discount: 0, discountPct: 0, serviceChargeWaived: false, discountAmount: 0,
      partDiscount: 0, partServiceChargeWaived: 0,
    };
  };

  const acme: Contact = { id: 'acme', code: 'C-0001', name: 'Acme Retail' } as Contact;

  function page(options: { restaurant?: PosRestaurant; till?: PosTill; preview?: PosOrderBillPreview; order?: PosOrder } = {}) {
    vi.useFakeTimers();
    const previews: PosOrderBillRequest[] = [];
    const bills: CreatePosOrderInvoiceRequest[] = [];
    let paid = false;

    const restaurantService = {
      getOrder: (): Observable<PosOrder> => of(paid ? order({ billed: 316, toBill: 2 }) : (options.order ?? order())),
      getRestaurant: (): Observable<PosRestaurant> => of(options.restaurant ?? restaurant()),
      previewBill: (_o: string, _id: string, request: PosOrderBillRequest): Observable<PosOrderBillPreview> => {
        previews.push(request);
        if (options.preview) return of(options.preview);
        return of(paid ? preview('rest') : request.split === 'Whole' ? preview('whole') : preview('first'));
      },
      billOrder: (_o: string, _id: string, request: CreatePosOrderInvoiceRequest): Observable<CreatePosOrderInvoiceResult> => {
        bills.push(request);
        paid = true;
        return of({
          id: 'inv-1', code: 'INV0001', grandTotal: 316, serviceCharge: 20, packagingFee: 0, roundOff: -0.4, tendered: 500, changeAmount: 184,
          creditAmount: 0, isAbbreviatedTaxInvoice: false, orderStatus: 'Open', orderToBill: 317,
        });
      },
    };
    const posService = {
      getTill: (): Observable<PosTill> => of(options.till ?? till),
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
        { provide: ContactsService, useValue: { listContacts: () => of({ items: [acme], page: 1, pageSize: 8, totalCount: 1 }) } },
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
      press: (label: string) => {
        [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(label))!.click();
        render();
      },
      takePayment: () => element.querySelector<HTMLButtonElement>('button.btn-success')!,
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

    expect(p.previews[0]).toEqual({ split: 'Whole', items: [], parts: null, discountPct: null, discountAmount: null, waiveServiceCharge: false });
    expect(p.text()).toContain('Round off0.20This bill633.00');
    expect(p.text()).toContain('Service charge40.00');
    expect(p.text()).toContain('This bill settles the order.');
    expect(p.text()).toContain('Due 633.00');
  });

  it('asks for one of N equal parts, defaulting N to the guests', () => {
    const p = page();

    p.click('#pos-bill-split-equal');

    expect(p.previews.at(-1)).toEqual({ split: 'Equal', items: [], parts: 2, discountPct: null, discountAmount: null, waiveServiceCharge: false });
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
      discountPct: null, discountAmount: null, waiveServiceCharge: false,
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

  it('hands focus to the amount field when a tender is removed, prefilled with what is left', () => {
    const p = page();
    const amount = () => p.element.querySelector<HTMLInputElement>('#pos-bill-amount')!;
    const due = Number(amount().value);

    p.click('#pos-bill-mode-card');
    p.change('#pos-bill-amount', '100');
    p.press('Add');
    p.click('#pos-bill-mode-cash');
    p.change('#pos-bill-amount', '50');
    p.press('Add');
    // As a keyboard user does: focus the button, then press it.
    const remove = p.element.querySelector<HTMLButtonElement>('[aria-label="Remove Card tender"]')!;
    remove.focus();
    expect(document.activeElement).toBe(remove);
    remove.click();
    p.render();

    // Phase 85 -- the button went with its tender; the keyboard lands where the next tender is entered.
    expect(p.element.querySelector('[aria-label="Remove Card tender"]')).toBeNull();
    expect(document.activeElement).toBe(amount());
    expect(amount().value).toBe((due - 50).toFixed(2));
    expect(p.text()).toContain('Card 100.00 removed.');
  });

  // ---- Phase 72: a customer per bill, and credit ----------------------------------------------------

  const creditTill = (canSellOnCredit: boolean): PosTill =>
    ({ ...till, canSellOnCredit, walkInCustomer: { id: 'walk-in', code: 'WALKIN', name: 'Cash Customer' } }) as PosTill;

  it('bills a part to a guest named for it and leaves the rest on their account once the cashier ticks it', () => {
    const p = page({ till: creditTill(true) });
    expect(p.text()).toContain('Bill to: Cash Customer (walk-in)');

    p.press('Change Customer');
    p.change('#pos-bill-customer-search', 'Acme');
    p.press('Acme Retail (C-0001)');
    expect(p.text()).toContain('Bill to: Acme Retail');

    p.click('#pos-bill-mode-card');
    p.change('#pos-bill-amount', '100');
    p.press('Add');
    expect(p.takePayment().disabled).toBe(true);

    p.change('#pos-bill-credit-accept', true);
    expect(p.text()).toContain('Leave 533.00 on Acme Retail\'s account');
    p.takePayment().click();
    p.render();

    expect(p.bills[0]).toMatchObject({ contactId: 'acme', tenders: [{ paymentModeId: 'card', amount: 100 }], changeAmount: 0 });
  });

  it('lets a named guest put the whole bill on their account with nothing handed over', () => {
    const p = page({ till: creditTill(true) });

    p.press('Change Customer');
    p.change('#pos-bill-customer-search', 'Acme');
    p.press('Acme Retail (C-0001)');
    p.change('#pos-bill-credit-accept', true);
    p.takePayment().click();
    p.render();

    expect(p.bills[0]).toMatchObject({ contactId: 'acme', tenders: [], changeAmount: 0 });
  });

  it('will not leave anything on the walk-in, and names the key a cashier without Approve lacks', () => {
    const p = page({ till: creditTill(false) });

    p.click('#pos-bill-mode-card');
    p.change('#pos-bill-amount', '100');
    p.press('Add');
    expect(p.text()).toContain('The walk-in customer cannot take credit.');
    expect(p.element.querySelector('#pos-bill-credit-accept')).toBeNull();

    p.press('Change Customer');
    p.change('#pos-bill-customer-search', 'Acme');
    p.press('Acme Retail (C-0001)');
    expect(p.text()).toContain('Sales.Invoice.Approve');
    expect(p.element.querySelector('#pos-bill-credit-accept')).toBeNull();
    expect(p.takePayment().disabled).toBe(true);
  });

  it('shows what the order\'s discounts took off this bill, and a waived service charge', () => {
    const p = page({
      preview: {
        ...preview('whole'), amount: 456, serviceCharge: 0, vat: 59.28, unrounded: 515.28, roundOff: -0.28, total: 515, orderTotal: 515,
        gross: 520, discount: 64, discountPct: 5, serviceChargeWaived: true, discountAmount: 0,
      },
    });

    expect(p.text()).toContain('Gross520.00');
    expect(p.text()).toContain('Discount (5% on the order)64.00');
    expect(p.text()).toContain('Service chargeWaived');
  });

  // ---- Phase 83: a discount on one guest's bill --------------------------------------------------------

  it('prices one guest\'s discount on the server, shows it apart from the order\'s, sends it, and clears it after', () => {
    const p = page({
      preview: {
        ...preview('first'), amount: 234, vat: 32.76, unrounded: 284.76, roundOff: 0.24, total: 285, gross: 260,
        discount: 26, partDiscount: 26,
      },
    });

    p.change('#pos-bill-discount-kind', 'Amount');
    p.change('#pos-bill-discount', '26');
    expect(p.previews.at(-1)).toEqual({ split: 'Whole', items: [], parts: null, discountPct: null, discountAmount: 26, waiveServiceCharge: false });
    expect(p.text()).toContain('Discount on this bill26.00');
    expect(p.text()).not.toContain('on the order)');

    p.change('#pos-bill-discount-kind', 'Percent');
    expect(p.previews.at(-1)).toMatchObject({ discountPct: 26, discountAmount: null });
    p.change('#pos-bill-discount-kind', 'Amount');

    p.click('#pos-bill-mode-cash');
    p.change('#pos-bill-amount', '300');
    p.press('Add');
    p.takePayment().click();
    p.render();

    expect(p.bills[0]).toMatchObject({ discountPct: null, discountAmount: 26 });
    // The next part is another guest's: it carries no discount until one is given for it.
    expect(p.element.querySelector<HTMLInputElement>('#pos-bill-discount')!.value).toBe('');
    expect(p.previews.at(-1)).toMatchObject({ discountPct: null, discountAmount: null });
    expect(p.text()).toContain('26.00 off this bill.');
  });

  it('disables the bill\'s discount for a cashier without Sales.Invoice.TillDiscount and names the key', () => {
    const p = page({ restaurant: restaurant({ canDiscount: false }) });

    expect(p.element.querySelector<HTMLInputElement>('#pos-bill-discount')!.disabled).toBe(true);
    expect(p.element.querySelector<HTMLSelectElement>('#pos-bill-discount-kind')!.disabled).toBe(true);
    expect(p.text()).toContain('Giving a discount needs the Sales.Invoice.TillDiscount permission at this location.');
    expect(p.previews.at(-1)).toMatchObject({ discountPct: null, discountAmount: null });
  });

  it('says a waiter without a drawer cannot take payment, before anything is chosen', () => {
    const p = page({ restaurant: restaurant({ mySessionId: null, mySessionCode: null }) });

    expect(p.text()).toContain('Taking payment needs your own open session at this till');
    expect(p.element.querySelector('#pos-bill-split-whole')).toBeNull();
  });
  // ---- Phase 91: one guest declining the service charge -------------------------------------------------

  it('prices one guest declining the service charge on the server, sends it, and charges the next guest again', () => {
    const p = page({
      preview: { ...preview('first'), serviceCharge: 0, vat: 33.8, unrounded: 293.8, roundOff: 0.2, total: 294, partServiceChargeWaived: 20 },
    });

    const box = p.element.querySelector<HTMLInputElement>('#pos-bill-waive-service-charge')!;
    expect(box.labels?.[0]?.textContent?.trim()).toBe('Waive service charge on this bill');
    expect(box.checked).toBe(false);

    box.click();
    p.render();
    expect(p.previews.at(-1)).toMatchObject({ waiveServiceCharge: true });
    expect(p.text()).toContain('Service chargeWaived on this bill (20.00)');

    p.click('#pos-bill-mode-cash');
    p.change('#pos-bill-amount', '300');
    p.press('Add');
    p.takePayment().click();
    p.render();

    expect(p.bills[0]).toMatchObject({ waiveServiceCharge: true });
    // The next part is another guest's: they pay the service charge unless they decline it too.
    expect(p.element.querySelector<HTMLInputElement>('#pos-bill-waive-service-charge')!.checked).toBe(false);
    expect(p.previews.at(-1)).toMatchObject({ waiveServiceCharge: false });
  });

  it('disables the waiver for a cashier without Sales.Invoice.TillRemoveServiceCharge and names the key', () => {
    const p = page({ restaurant: restaurant({ canRemoveServiceCharge: false }) });

    expect(p.element.querySelector<HTMLInputElement>('#pos-bill-waive-service-charge')!.disabled).toBe(true);
    expect(p.text()).toContain('Waiving the service charge needs the Sales.Invoice.TillRemoveServiceCharge permission at this location.');
  });

  it('offers no waiver where the order charges none to decline: a take-away, or an order that waived it', () => {
    const takeAway = page({ order: order({ orderType: 'TakeAway' }) });
    expect(takeAway.element.querySelector('#pos-bill-waive-service-charge')).toBeNull();
    TestBed.resetTestingModule();

    const waived = page({ order: order({ serviceChargeWaived: true }) });
    expect(waived.element.querySelector('#pos-bill-waive-service-charge')).toBeNull();
  });
});
