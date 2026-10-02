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
  PosOrderItemInput,
  PosOrderLineQuantityInput,
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
    ...overrides,
  });

  const ticket = (sendNumber: number, station: string, lines: [string, number][]): KitchenTicket => ({
    id: `k-${sendNumber}-${station}`, sendNumber, number: `ORD0001-${sendNumber}`, kitchenStationId: station === 'Default' ? null : 'kitchen',
    kitchenStationName: station, isCancellation: lines.every(([, q]) => q < 0), reason: null, createdAt: '2026-10-02T04:00:00Z',
    createdByName: 'Sita', printCount: 0,
    lines: lines.map(([name, quantity], i) => ({ orderLineId: `l-${name}`, lineNo: i + 1, productName: name, unitName: 'plt', quantity, note: null })),
  });

  const order = (overrides: Partial<PosOrder> = {}): PosOrder => ({
    id: 'ord-1', code: 'ORD0001', locationId, locationCode: '1002', locationName: 'POS Restaurant', orderType: 'DineIn',
    status: 'Open', tableId: 't1', tableName: 'T1', areaId: 'gf', areaName: 'Ground Floor', covers: 2, contactId: null,
    contactName: 'Cash Customer', date: '2026-10-02', createdAt: '2026-10-02T04:00:00Z', createdByName: 'Sita',
    voidReason: null, voidedAt: null, voidedByName: null, amount: 460, serviceCharge: 40, vat: 65, total: 565, outstanding: 3,
    lines: [
      { id: 'l-Chicken Momo', lineNo: 1, productId: 'momo', productCode: 'P0002', productName: 'Chicken Momo', unitId: 'plate',
        unitName: 'plt', rate: 200, vatRate: 'ThirteenPercentVat', serviceChargeRate: 10, note: null, kitchenStationId: 'kitchen',
        kitchenStationName: 'Kitchen', ordered: 2, discarded: 0, quantity: 2, served: 0, outstanding: 2, amount: 400,
        serviceChargeAmount: 40, vatAmount: 57.2, total: 497.2 },
      { id: 'l-Coke 250ml', lineNo: 2, productId: 'coke', productCode: 'P0003', productName: 'Coke 250ml', unitId: 'plate',
        unitName: 'plt', rate: 60, vatRate: 'ThirteenPercentVat', serviceChargeRate: 0, note: null, kitchenStationId: null,
        kitchenStationName: 'Default', ordered: 1, discarded: 0, quantity: 1, served: 0, outstanding: 1, amount: 60,
        serviceChargeAmount: 0, vatAmount: 7.8, total: 67.8 },
    ],
    tickets: [ticket(1, 'Kitchen', [['Chicken Momo', 2]]), ticket(1, 'Default', [['Coke 250ml', 1]])],
    ...overrides,
  });

  function page(options: { orderId?: string; query?: Record<string, string>; restaurant?: PosRestaurant; order?: PosOrder } = {}) {
    const created: CreatePosOrderRequest[] = [];
    const added: { newItems: PosOrderItemInput[]; moreOf: PosOrderLineQuantityInput[] }[] = [];
    const discarded: { items: PosOrderLineQuantityInput[]; reason: string }[] = [];
    const printed: string[] = [];
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
      discard: (_o: string, _id: string, items: PosOrderLineQuantityInput[], reason: string): Observable<PosOrder> => {
        discarded.push({ items, reason });
        return of(order());
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
      element, created, added, discarded, printed, navigate,
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

  it('will not send a delivery without a named customer', () => {
    const p = page({ query: { locationId, type: 'Delivery' } });

    p.press('Chicken Momo');

    const send = [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === 'Send to Kitchen')!;
    expect(send.disabled).toBe(true);
    expect(send.getAttribute('aria-describedby')).toBe('pos-order-customer-needed');
    expect(p.text()).toContain('A delivery goes to a named customer');
  });
});
