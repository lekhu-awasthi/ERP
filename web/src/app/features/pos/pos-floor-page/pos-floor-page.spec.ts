import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PosOpenOrder, PosRestaurant } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosFloorPage } from './pos-floor-page';

/** Phase 64 -- the restaurant till's floor: free tables open a new order, taken ones open theirs. */
describe('PosFloorPage', () => {
  const organizationId = 'org-1';
  const locationId = 'loc-1';

  const seated: PosOpenOrder = {
    id: 'ord-1', code: 'ORD0001', orderType: 'DineIn', tableId: 't2', covers: 2, contactName: 'Cash Customer',
    createdAt: '2026-10-02T04:00:00Z', outstanding: 1, total: 565,
  };
  const parcel: PosOpenOrder = { ...seated, id: 'ord-2', code: 'ORD0002', orderType: 'TakeAway', tableId: null, covers: 0, outstanding: 0 };

  const restaurant: PosRestaurant = {
    locationId, locationCode: '1002', locationName: 'POS Restaurant', availableTabs: ['DineIn', 'TakeAway', 'Delivery'],
    defaultTab: 'DineIn', serviceChargeEnabled: true, serviceChargeRate: 10, printKot: true, canvasWidth: 1100, canvasHeight: 800,
    walkInCustomer: null, categories: [],
    areas: [
      { id: 'gf', name: 'Ground Floor', tables: [
        { id: 't1', name: 'T1', capacity: 4, shape: 'Rectangle', x: 110, y: 80, width: 220, height: 160, order: null },
        { id: 't2', name: 'T2', capacity: 2, shape: 'Circle', x: 450, y: 100, width: 100, height: 100, order: seated },
      ] },
      { id: 'rt', name: 'Rooftop', tables: [
        { id: 'r1', name: 'R1', capacity: 6, shape: 'Rectangle', x: 0, y: 0, width: 200, height: 100, order: null },
      ] },
    ],
    orders: [seated, parcel], canVoid: true,
  };

  function page() {
    let loads = 0;
    TestBed.configureTestingModule({
      imports: [PosFloorPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PosRestaurantService,
          useValue: { getRestaurant: (): Observable<PosRestaurant> => { loads++; return of(restaurant); } },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId, locationId }) } } },
      ],
    });

    const fixture = TestBed.createComponent(PosFloorPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    return {
      element,
      loads: () => loads,
      render,
      link: (text: string) => [...element.querySelectorAll<HTMLAnchorElement>('a')].find((a) => a.textContent?.includes(text)),
      choose: (id: string) => {
        element.querySelector<HTMLInputElement>(`#${id}`)!.dispatchEvent(new Event('change'));
        render();
      },
    };
  }

  it('draws the area’s tables where the floor plan put them, a taken one carrying its order', () => {
    const p = page();

    const free = p.link('T1')!;
    expect(free.getAttribute('href')).toBe(`/organizations/${organizationId}/pos/orders/new?locationId=${locationId}&type=DineIn&tableId=t1`);
    expect(free.textContent).toContain('free, new order');
    expect((free.parentElement as HTMLElement).style.getPropertyValue('--x')).toBe('10%');

    const taken = p.link('T2')!;
    expect(taken.getAttribute('href')).toBe(`/organizations/${organizationId}/pos/orders/ord-1`);
    expect(taken.textContent).toContain('ORD0001');
    expect(taken.textContent).toContain('1 to serve');
  });

  it('switches areas, and lists Take Away orders on their own tab', () => {
    const p = page();

    p.choose('pos-floor-area-rt');
    expect(p.link('R1')).toBeDefined();
    expect(p.link('T1')).toBeUndefined();

    p.choose('pos-floor-tab-TakeAway');
    expect(p.link('ORD0002')!.getAttribute('href')).toBe(`/organizations/${organizationId}/pos/orders/ord-2`);
    expect(p.link('ORD0001')).toBeUndefined();
    expect(p.link('New Take Away Order')!.getAttribute('href')).toContain('type=TakeAway');
  });

  it('refreshes from the server rather than guessing who sat down', () => {
    const p = page();

    [...p.element.querySelectorAll('button')].find((b) => b.textContent?.includes('Refresh'))!.click();
    p.render();

    expect(p.loads()).toBe(2);
  });
});
