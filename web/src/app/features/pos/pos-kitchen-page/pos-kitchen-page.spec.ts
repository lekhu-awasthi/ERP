import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import {
  PosKitchenBoard,
  PosKitchenBoardTicket,
  PosKitchenBoardView,
  PosKitchenServeResult,
  PosOrderLineQuantityInput,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { KITCHEN_POLL_MS, PosKitchenPage } from './pos-kitchen-page';

/** Phase 65 -- the kitchen board: one card per ticket, served from the card, polled while on screen. */
describe('PosKitchenPage', () => {
  const ticket = (id: string, pending: number, overrides: Partial<PosKitchenBoardTicket> = {}): PosKitchenBoardTicket => ({
    id, number: `ORD0001-${id}`, orderId: 'ord-1', orderCode: 'ORD0001', orderType: 'DineIn', orderStatus: 'Open',
    label: 'T1', areaName: 'Ground Floor', covers: 2, kitchenStationId: 'kitchen', kitchenStationName: 'Kitchen',
    state: pending > 0 ? 'Pending' : 'Served', reason: null, createdAt: new Date(Date.now() - 12 * 60_000).toISOString(),
    createdByName: 'Sita', kind: 'Send', counterpartOrderCode: null,
    lines: [{ orderLineId: 'momo', productName: 'Chicken Momo', unitName: null, note: 'less spicy', sent: 2, served: 2 - pending,
      cancelled: 0, pending, moved: 0, isTakeAway: false }],
    ...overrides,
  });

  const board = (tickets: PosKitchenBoardTicket[]): PosKitchenBoard => ({
    locationId: 'loc-1', locationName: 'POS Restaurant', stations: [{ id: null, name: 'Default' }, { id: 'kitchen', name: 'Kitchen' }],
    pendingCount: tickets.filter((t) => t.state === 'Pending').length, tickets,
    summary: [{ productName: 'Chicken Momo', unitName: null, pending: tickets.reduce((s, t) => s + t.lines[0].pending, 0) }],
    version: '2026-10-02T05:00:00Z', readAt: new Date().toISOString(),
  });

  function page() {
    vi.useFakeTimers();
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    const reads: { view: PosKitchenBoardView; station: string | null }[] = [];
    const serves: { ticketId: string; items: PosOrderLineQuantityInput[] }[] = [];
    let current = board([ticket('1', 2)]);

    const service = {
      getKitchenBoard: (_o: string, _l: string, filter: { view: PosKitchenBoardView; station: string | null }): Observable<PosKitchenBoard> => {
        reads.push({ view: filter.view, station: filter.station });
        return of(current);
      },
      serveTicket: (_o: string, _order: string, ticketId: string, items: PosOrderLineQuantityInput[]): Observable<PosKitchenServeResult> => {
        serves.push({ ticketId, items });
        current = board([]);
        return of({ orderId: 'ord-1', ticketId, state: 'Served', pending: 0 });
      },
    };

    TestBed.configureTestingModule({
      imports: [PosKitchenPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosRestaurantService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'org-1', locationId: 'loc-1' }) } } },
      ],
    });

    const fixture = TestBed.createComponent(PosKitchenPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    return {
      element, reads, serves, render, fixture,
      setBoard: (b: PosKitchenBoard) => (current = b),
      text: () => element.textContent?.replace(/\s+/g, ' ') ?? '',
      button: (label: string) => [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes(label))!,
    };
  }

  afterEach(() => vi.useRealTimers());

  it('shows each pending ticket with its table, note and how long it has waited', () => {
    const p = page();

    expect(p.reads[0]).toEqual({ view: 'Pending', station: null });
    expect(p.text()).toContain('T1');
    expect(p.text()).toContain('2 × Chicken Momo');
    expect(p.text()).toContain('» less spicy');
    expect(p.text()).toContain('Waiting 12 min');
    expect(p.text()).toContain('Pending (1)');
  });

  it('serves a whole ticket from its card, and moves focus to the board when the card leaves', () => {
    const p = page();

    p.button('All Served').click();
    p.render();

    expect(p.serves).toEqual([{ ticketId: '1', items: [] }]);
    expect(p.text()).toContain('Nothing is waiting to be cooked.');
  });

  it('serves one line with what the ticket still has to cook', () => {
    const p = page();
    p.setBoard(board([ticket('1', 1)]));
    p.button('Refresh').click();
    p.render();

    p.button('Serve').click();
    p.render();

    expect(p.serves[0]).toEqual({ ticketId: '1', items: [{ lineId: 'momo', quantity: 1 }] });
  });

  it('reads again on its own while on screen, and announces only tickets it had not seen', () => {
    const p = page();
    const first = p.reads.length;

    p.setBoard(board([ticket('1', 2), ticket('2', 1, { label: 'T2' })]));
    vi.advanceTimersByTime(KITCHEN_POLL_MS);
    p.render();

    expect(p.reads.length).toBe(first + 1);
    expect(p.element.querySelector('[role="status"]')!.textContent).toContain('1 new kitchen ticket.');
  });

  it('filters by station', () => {
    const p = page();

    const select = p.element.querySelector<HTMLSelectElement>('#pos-kitchen-station')!;
    select.value = 'default';
    select.dispatchEvent(new Event('change'));
    p.render();

    expect(p.reads.at(-1)).toEqual({ view: 'Pending', station: 'default' });
  });
});
