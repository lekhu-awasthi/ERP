import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { PosOrder, PosOrderStatus } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosOrderListPage } from './pos-order-list-page';

/** Phase 64 -- the ERP's read-only view of every restaurant order. */
describe('PosOrderListPage', () => {
  const order: PosOrder = {
    id: 'ord-1', code: 'ORD0001', locationId: 'loc-1', locationCode: '1002', locationName: 'POS Restaurant', orderType: 'DineIn',
    status: 'Open', tableId: 't1', tableName: 'T1', areaId: 'gf', areaName: 'Ground Floor', covers: 2, contactId: null,
    contactName: 'Cash Customer', date: '2026-10-02', createdAt: '2026-10-02T04:00:00Z', createdByName: 'Sita',
    voidReason: null, voidedAt: null, voidedByName: null, amount: 400, serviceCharge: 40, vat: 57.2, total: 497.2, outstanding: 1,
    settledAt: null, billed: 0, toBill: 2, invoices: [],
    lines: [{ id: 'l1', lineNo: 1, productId: 'momo', productCode: 'P0002', productName: 'Chicken Momo', unitId: null, unitName: null,
      rate: 200, vatRate: 'ThirteenPercentVat', serviceChargeRate: 10, note: 'less spicy', kitchenStationId: 'k',
      kitchenStationName: 'Kitchen', ordered: 3, discarded: 1, quantity: 2, served: 1, outstanding: 1, invoiced: 0, toBill: 2, amount: 400,
      serviceChargeAmount: 40, vatAmount: 57.2, total: 497.2, isTakeAway: false, parcelledFromLineId: null, movedIn: 0, movedOut: 0 }],
    tickets: [],
  };

  function page() {
    const calls: { status?: PosOrderStatus }[] = [];
    TestBed.configureTestingModule({
      imports: [PosOrderListPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PosRestaurantService,
          useValue: {
            listOrders: (_o: string, filter: { status?: PosOrderStatus }): Observable<PagedResult<PosOrder>> => {
              calls.push({ status: filter.status });
              return of({ items: [order], page: 1, pageSize: 20, totalCount: 1 });
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'org-1' }) } } },
      ],
    });

    const fixture = TestBed.createComponent(PosOrderListPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();
    return { element, calls, render };
  }

  it('opens on the open orders and expands a row to its counters in place', () => {
    const p = page();
    expect(p.calls[0]).toEqual({ status: 'Open' });

    const toggle = [...p.element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('ORD0001'))!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    p.render();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const detail = p.element.querySelector('#pos-order-detail-ord-1')!;
    const cells = [...detail.querySelectorAll('tbody td')].map((td) => td.textContent?.trim());
    expect(cells.slice(1, 5)).toEqual(['3', '1', '2', '1']);
  });

  it('shows every status when asked', () => {
    const p = page();

    [...p.element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'All')!.click();
    p.render();

    expect(p.calls.at(-1)).toEqual({ status: undefined });
  });
});
