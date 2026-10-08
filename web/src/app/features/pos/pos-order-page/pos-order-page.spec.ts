import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { PosProduct } from '../../../core/pos/pos.models';
import {
  CreatePosOrderRequest,
  KitchenTicket,
  PosKitchenTicketPrint,
  PosOrder,
  PosOrderBillPreview,
  PosOrderBillRequest,
  PosOrderItemInput,
  PosOrderLineQuantityInput,
  PosOrderTransferResult,
  PosRestaurant,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosOrderPage } from './pos-order-page';

/**
 * Phase 64 -- the order screen on phase 59's table: 2 Chicken Momo (the Kitchen) and a Coke (Default)
 * at T1, then one more Coke as a second send. Nothing leaves the browser until Send to Kitchen.
 */
describe('PosOrderPage', () => {
  const organizationId = 'org-1';
  const locationId = 'loc-1';

  const momo: PosProduct = {
    id: 'momo', code: 'P0002', name: 'Chicken Momo', categoryId: 'food', type: 'Service', vatRate: 'ThirteenPercentVat',
    rate: 200, serviceChargeApplicable: true, batchTracking: false, serialTracking: false, barcode: null,
    units: [{ unitId: 'plate', shortName: 'plt', conversionRate: 1, rate: 200 }],
  };
  const coke: PosProduct = { ...momo, id: 'coke', code: 'P0003', name: 'Coke 250ml', rate: 60, serviceChargeApplicable: false };

  const restaurant = (overrides: Partial<PosRestaurant> = {}): PosRestaurant => ({
    locationId, locationCode: '1002', locationName: 'POS Restaurant', availableTabs: ['DineIn', 'TakeAway', 'Delivery'],
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, printKot: false, canvasWidth: 1100,
    canvasHeight: 800, walkInCustomer: { id: 'walk-in', code: 'WALKIN', name: 'Cash Customer' }, categories: [],
    areas: [{ id: 'gf', name: 'Ground Floor', tables: [
      { id: 't1', name: 'T1', capacity: 4, shape: 'Rectangle', x: 100, y: 100, width: 250, height: 100, order: null },
      { id: 't2', name: 'T2', capacity: 2, shape: 'Circle', x: 450, y: 100, width: 100, height: 100, order: null },
    ] }],
    orders: [], canVoid: true,
    roundOffEnabled: true, printEstimateBill: true, printInvoice: true, mySessionId: null, mySessionCode: null,
    canKitchen: true, serviceChargeOnTakeAway: true,
    ...overrides,
  });

  const ticket = (sendNumber: number, station: string, lines: [string, number][]): KitchenTicket => ({
    id: `k-${sendNumber}-${station}`, sendNumber, number: `ORD0001-${sendNumber}`, kitchenStationId: station === 'Default' ? null : 'kitchen',
    kitchenStationName: station, isCancellation: lines.every(([, q]) => q < 0), reason: null, createdAt: '2026-10-02T04:00:00Z',
    createdByName: 'Sita', printCount: 0, kind: lines.every(([, q]) => q < 0) ? 'Cancellation' : 'Send', counterpartOrderCode: null,
    lines: lines.map(([name, quantity], i) => ({
      orderLineId: `l-${name}`, lineNo: i + 1, productName: name, unitName: 'plt', quantity, note: null, isTakeAway: false })),
  });

  const order = (overrides: Partial<PosOrder> = {}): PosOrder => ({
    id: 'ord-1', code: 'ORD0001', locationId, locationCode: '1002', locationName: 'POS Restaurant', orderType: 'DineIn',
    status: 'Open', tableId: 't1', tableName: 'T1', areaId: 'gf', areaName: 'Ground Floor', covers: 2, contactId: null,
    contactName: 'Cash Customer', date: '2026-10-02', createdAt: '2026-10-02T04:00:00Z', createdByName: 'Sita',
    voidReason: null, voidedAt: null, voidedByName: null, amount: 460, serviceCharge: 40, vat: 65, total: 565, outstanding: 3,
    settledAt: null, billed: 0, toBill: 3, invoices: [],
    lines: [
      { id: 'l-Chicken Momo', lineNo: 1, productId: 'momo', productCode: 'P0002', productName: 'Chicken Momo', unitId: 'plate',
        unitName: 'plt', rate: 200, vatRate: 'ThirteenPercentVat', serviceChargeRate: 10, note: null, kitchenStationId: 'kitchen',
        kitchenStationName: 'Kitchen', ordered: 2, discarded: 0, quantity: 2, served: 0, outstanding: 2, invoiced: 0, toBill: 2, amount: 400,
        serviceChargeAmount: 40, vatAmount: 57.2, total: 497.2, isTakeAway: false, parcelledFromLineId: null, movedIn: 0, movedOut: 0 },
      { id: 'l-Coke 250ml', lineNo: 2, productId: 'coke', productCode: 'P0003', productName: 'Coke 250ml', unitId: 'plate',
        unitName: 'plt', rate: 60, vatRate: 'ThirteenPercentVat', serviceChargeRate: 0, note: null, kitchenStationId: null,
        kitchenStationName: 'Default', ordered: 1, discarded: 0, quantity: 1, served: 0, outstanding: 1, invoiced: 0, toBill: 1, amount: 60,
        serviceChargeAmount: 0, vatAmount: 7.8, total: 67.8, isTakeAway: false, parcelledFromLineId: null, movedIn: 0, movedOut: 0 },
    ],
    tickets: [ticket(1, 'Kitchen', [['Chicken Momo', 2]]), ticket(1, 'Default', [['Coke 250ml', 1]])],
    ...overrides,
  });

  function page(options: { orderId?: string; query?: Record<string, string>; restaurant?: PosRestaurant; order?: PosOrder } = {}) {
    const created: CreatePosOrderRequest[] = [];
    const added: { newItems: PosOrderItemInput[]; moreOf: PosOrderLineQuantityInput[] }[] = [];
    const discarded: { items: PosOrderLineQuantityInput[]; reason: string }[] = [];
    const printed: string[] = [];
    const previews: PosOrderBillRequest[] = [];
    const marked: { lineId: string; quantity: number }[] = [];
    const transfers: { tableId: string; items: PosOrderLineQuantityInput[] }[] = [];
    let latest = options.order ?? order();
    const service = {
      getRestaurant: (): Observable<PosRestaurant> => of(options.restaurant ?? restaurant()),
      getOrder: (): Observable<PosOrder> => of(options.order ?? order()),
      listProducts: (): Observable<PagedResult<PosProduct>> =>
        of({ items: [momo, coke], page: 1, pageSize: 48, totalCount: 2 }),
      createOrder: (_o: string, request: CreatePosOrderRequest): Observable<PosOrder> => {
        created.push(request);
        return of(order());
      },
      addItems: (_o: string, _id: string, newItems: PosOrderItemInput[], moreOf: PosOrderLineQuantityInput[]): Observable<PosOrder> => {
        added.push({ newItems, moreOf });
        const base = order();
        latest = { ...base, tickets: [...base.tickets, ticket(2, 'Default', [['Coke 250ml', 1]])] };
        return of(latest);
      },
      markTakeAway: (_o: string, _id: string, lineId: string, quantity: number): Observable<PosOrder> => {
        marked.push({ lineId, quantity });
        const base = order();
        const parcel: KitchenTicket = {
          ...ticket(2, 'Kitchen', [['Chicken Momo', -1], ['Chicken Momo', 1]]), id: 'k-take-away', kind: 'TakeAway', isCancellation: false,
        };
        latest = { ...base, tickets: [...base.tickets, parcel] };
        return of(latest);
      },
      transferItems: (_o: string, _id: string, tableId: string, items: PosOrderLineQuantityInput[]): Observable<PosOrderTransferResult> => {
        transfers.push({ tableId, items });
        const emptied = items.length === 2;
        const target = order({
          id: 'ord-2', code: 'ORD0002', tableId, tableName: 'T2',
          tickets: [{ ...ticket(1, 'Kitchen', [['Chicken Momo', 1]]), id: 'k-in', kind: 'Transfer', counterpartOrderCode: 'ORD0001' }],
        });
        return of({ source: order(emptied ? { status: 'Voided' } : {}), target, targetCreated: true });
      },
      discard: (_o: string, _id: string, items: PosOrderLineQuantityInput[], reason: string): Observable<PosOrder> => {
        discarded.push({ items, reason });
        return of(order());
      },
      previewBill: (_o: string, _id: string, request: PosOrderBillRequest): Observable<PosOrderBillPreview> => {
        previews.push(request);
        return of({
          orderId: 'ord-1', orderCode: 'ORD0001', amount: 460, serviceCharge: 40, vat: 65, unrounded: 565, roundOff: 0,
          total: 565, billsTheRest: true, orderTotal: 565, billedBefore: 0, leftAfter: 0,
          lines: [{ orderLineId: 'l-Chicken Momo', lineNo: 1, productName: 'Chicken Momo', unitName: 'plt', note: null,
            quantity: 2, rate: 200, serviceChargeRate: 10, amount: 400, serviceChargeAmount: 40, vatAmount: 57.2,
            total: 497.2, remainingAfter: 0 }],
        });
      },
      printTicket: (_o: string, _id: string, ticketId: string): Observable<PosKitchenTicketPrint> => {
        printed.push(ticketId);
        return of({ order: latest, ticketId, printNumber: 1 });
      },
    };

    TestBed.configureTestingModule({
      imports: [PosOrderPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosRestaurantService, useValue: service },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ id: organizationId, orderId: options.orderId ?? 'new' })),
            snapshot: {
              paramMap: convertToParamMap({ id: organizationId, orderId: options.orderId ?? 'new' }),
              queryParamMap: convertToParamMap(options.query ?? { locationId, type: 'DineIn', tableId: 't1' }),
            },
          },
        },
      ],
    });

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(PosOrderPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    return {
      element, created, added, discarded, printed, previews, navigate, marked, transfers,
      text: () => element.textContent?.replace(/\s+/g, ' ') ?? '',
      press: (label: string) => {
        [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(label))!.click();
        render();
      },
      type: (id: string, value: string) => {
        const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        render();
      },
      submitPanel: () => {
        element.querySelector('form.pos-panel')!.dispatchEvent(new Event('submit'));
        render();
      },
    };
  }

  beforeEach(() => {
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  it('seats a new table: items wait in the browser until Send, which opens the order and goes to it', () => {
    const p = page();

    expect(p.text()).toContain('New Dine In order · T1 (Ground Floor)');
    p.press('Chicken Momo');
    p.press('Chicken Momo');
    p.press('Coke 250ml');
    p.type('pos-order-covers', '3');
    expect(p.created).toEqual([]);
    expect(p.text()).toContain('To send (3)');

    p.press('Send to Kitchen');

    expect(p.created).toEqual([{
      locationId, orderType: 'DineIn', tableId: 't1', covers: 3, contactId: null,
      items: [
        { productId: 'momo', quantity: 2, unitId: null, note: null },
        { productId: 'coke', quantity: 1, unitId: null, note: null },
      ],
    }]);
    expect(p.navigate).toHaveBeenCalledWith(['/organizations', organizationId, 'pos', 'orders', 'ord-1'], { replaceUrl: true });
    expect(p.text()).toContain('Sent to the kitchen: Kitchen, Default (ticket ORD0001-1).');
  });

  it('sends one more of a sent line as more of that line, not a new one', () => {
    const p = page({ orderId: 'ord-1' });

    p.press('One More Coke 250ml');
    p.press('Send to Kitchen');

    expect(p.added).toEqual([{ newItems: [], moreOf: [{ lineId: 'l-Coke 250ml', quantity: 1 }] }]);
  });

  it('will not discard without a reason, and sends the reason with one', () => {
    const p = page({ orderId: 'ord-1' });

    p.press('Discard Chicken Momo');
    p.submitPanel();
    expect(p.discarded).toEqual([]);
    expect(p.text()).toContain('A discard needs a reason.');

    p.type('pos-order-discard-reason', 'Guest changed their mind');
    p.submitPanel();
    expect(p.discarded).toEqual([{ items: [{ lineId: 'l-Chicken Momo', quantity: 1 }], reason: 'Guest changed their mind' }]);
  });

  it('offers no Discard to a waiter without Pos.Order.Void, and says why', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ canVoid: false }) });

    expect([...p.element.querySelectorAll('button')].some((b) => b.textContent?.trim().startsWith('Discard'))).toBe(false);
    expect([...p.element.querySelectorAll('button')].some((b) => b.textContent?.trim().startsWith('Void Order'))).toBe(false);
    expect(p.text()).toContain('needs the Pos.Order.Void permission');
  });

  it('prints the new send’s tickets itself where the location prints KOTs', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ printKot: true }) });

    p.press('One More Coke 250ml');
    p.press('Send to Kitchen');

    expect(p.printed).toEqual(['k-2-Default']);
    expect(window.print).toHaveBeenCalled();
  });

  it('prints the estimate from the server\'s preview, saying it is not a tax invoice, and links to the bill', () => {
    const p = page({ orderId: 'ord-1' });

    const bill = p.element.querySelector<HTMLAnchorElement>('a[href$="/pos/orders/ord-1/bill"]');
    expect(bill?.textContent).toContain('Bill & Pay');

    p.press('Print Estimate');

    expect(p.previews).toEqual([{ split: 'Whole', items: [], parts: null }]);
    const paper = p.element.querySelector('.pos-print-root')!.textContent!.replace(/\s+/g, ' ');
    expect(paper).toContain('Estimate Bill');
    expect(paper).toContain('NOT A TAX INVOICE');
    expect(paper).toContain('Estimated Total565.00');
    expect(window.print).toHaveBeenCalled();
  });

  it('offers no estimate where the location does not print one', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ printEstimateBill: false }) });
    expect(p.text()).not.toContain('Print Estimate');
  });

  it('offers nothing to bill on a settled order', () => {
    const settled = page({ orderId: 'ord-1', order: order({ status: 'Settled', settledAt: '2026-10-02T05:00:00Z', toBill: 0 }) });
    expect(settled.text()).toContain('Settled');
    expect(settled.element.querySelector('a[href$="/bill"]')).toBeNull();
  });

  // ---- Phase 68: Mark as Take Away and Transfer Items ----------------------------------------------

  it('marks part of a dine-in line take away, saying first what happens to its service charge', () => {
    const p = page({ orderId: 'ord-1' });

    p.press('Take Away Chicken Momo');
    const quantity = p.element.querySelector<HTMLInputElement>('#pos-order-panel-first')!;
    expect(quantity.value).toBe('2');
    expect(quantity.getAttribute('aria-describedby')).toBe('pos-order-take-away-note');
    expect(p.text()).toContain('The parcel keeps its 10% service charge');

    p.type('pos-order-panel-first', '1');
    p.submitPanel();

    expect(p.marked).toEqual([{ lineId: 'l-Chicken Momo', quantity: 1 }]);
    expect(p.text()).toContain('Marked as take away');
  });

  it('says a parcel carries no service charge where the location switched it off', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ serviceChargeOnTakeAway: false }) });

    p.press('Take Away Chicken Momo');
    expect(p.text()).toContain('carries no service charge (this location\'s setting)');
  });

  it('prints the take-away ticket for the kitchen where the location prints KOTs', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ printKot: true }) });

    p.press('Take Away Chicken Momo');
    p.submitPanel();

    expect(p.printed).toEqual(['k-take-away']);
    const paper = p.element.querySelector('.pos-print-root')!.textContent!.replace(/\s+/g, ' ');
    expect(paper).toContain('Take Away: pack to go');
  });

  it('offers no Take Away on a Take Away order, or on a line already parcelled', () => {
    const parcel = page({ orderId: 'ord-1', order: order({ orderType: 'TakeAway', tableId: null, tableName: null }) });
    expect([...parcel.element.querySelectorAll('button')].some((b) => b.textContent?.trim().startsWith('Take Away'))).toBe(false);
    TestBed.resetTestingModule();

    const base = order();
    const marked = page({
      orderId: 'ord-1',
      order: order({ lines: base.lines.map((l) => ({ ...l, isTakeAway: true })) }),
    });
    expect([...marked.element.querySelectorAll('button')].some((b) => b.textContent?.trim().startsWith('Take Away'))).toBe(false);
    expect(marked.text()).toContain('Take away');
  });

  it('transfers ticked items to the chosen table, and asks for both first', () => {
    const p = page({ orderId: 'ord-1', restaurant: restaurant({ printKot: true }) });

    p.press('Transfer Items');
    p.submitPanel();
    expect(p.transfers).toEqual([]);
    expect(p.text()).toContain('Choose the table to transfer to.');

    const select = p.element.querySelector<HTMLSelectElement>('#pos-order-panel-first')!;
    expect([...select.options].map((o) => o.textContent?.trim())).toEqual([
      'Choose a table', 'T2 (Ground Floor) · free: opens a new order',
    ]);
    select.value = 't2';
    select.dispatchEvent(new Event('change'));
    p.submitPanel();
    expect(p.transfers).toEqual([]);
    expect(p.text()).toContain('Tick at least one item to transfer.');

    const pick = p.element.querySelector<HTMLInputElement>('#pos-order-transfer-pick-l-Chicken\\ Momo')!;
    pick.checked = true;
    pick.dispatchEvent(new Event('change'));
    p.type('pos-order-transfer-qty-l-Chicken\\ Momo', '1');
    p.submitPanel();

    expect(p.transfers).toEqual([{ tableId: 't2', items: [{ lineId: 'l-Chicken Momo', quantity: 1 }] }]);
    expect(p.text()).toContain('Moved 1 item(s) to T2 (ORD0002, a new order).');
    expect(p.printed).toEqual(['k-in']);
    expect(p.navigate).not.toHaveBeenCalled();
  });

  it('follows the food to the other table when a transfer emptied this order', () => {
    const p = page({ orderId: 'ord-1' });

    p.press('Transfer Items');
    const select = p.element.querySelector<HTMLSelectElement>('#pos-order-panel-first')!;
    select.value = 't2';
    select.dispatchEvent(new Event('change'));
    for (const box of p.element.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true;
      box.dispatchEvent(new Event('change'));
    }
    p.submitPanel();

    expect(p.navigate).toHaveBeenCalledWith(['/organizations', organizationId, 'pos', 'orders', 'ord-2'], { replaceUrl: true });
    expect(p.text()).toContain('ORD0001 is closed');
  });

  it('will not send a delivery without a named customer', () => {
    const p = page({ query: { locationId, type: 'Delivery' } });

    p.press('Chicken Momo');

    const send = [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === 'Send to Kitchen')!;
    expect(send.disabled).toBe(true);
    expect(send.getAttribute('aria-describedby')).toBe('pos-order-customer-needed');
    expect(p.text()).toContain('A delivery goes to a named customer');
  });
});
