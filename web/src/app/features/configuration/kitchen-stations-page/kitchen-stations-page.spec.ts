import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { Product } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { KitchenStation } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { KitchenStationsPage } from './kitchen-stations-page';

/** Phase 64 -- a station's products are chosen from the station and saved as one list. */
describe('KitchenStationsPage', () => {
  const stations: KitchenStation[] = [
    { id: 'kitchen', name: 'Kitchen', isActive: true, products: [{ id: 'momo', code: 'P0002', name: 'Chicken Momo' }] },
    { id: 'bar', name: 'Bar', isActive: true, products: [] },
  ];
  const product = (id: string, name: string): Product =>
    ({ id, code: id.toUpperCase(), name, availableForSale: true, isActive: true } as unknown as Product);

  function page() {
    const set: { stationId: string; productIds: string[] }[] = [];
    TestBed.configureTestingModule({
      imports: [KitchenStationsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PosRestaurantService,
          useValue: {
            listKitchenStations: (): Observable<KitchenStation[]> => of(stations),
            setKitchenStationProducts: (_o: string, stationId: string, productIds: string[]): Observable<KitchenStation[]> => {
              set.push({ stationId, productIds });
              return of(stations);
            },
          },
        },
        { provide: CatalogService, useValue: { listAllProducts: (): Observable<Product[]> => of([product('momo', 'Chicken Momo'), product('coke', 'Coke 250ml')]) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'org-1' }) } } },
      ],
    });

    const fixture = TestBed.createComponent(KitchenStationsPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();
    return { element, set, render };
  }

  it('sends the whole list for a station, and says where a product goes today', () => {
    const p = page();

    [...p.element.querySelectorAll('button')].find((b) => b.textContent?.includes('Choose Products for Bar'))!.click();
    p.render();
    expect(p.element.textContent).toContain('now at Kitchen');

    const coke = p.element.querySelector<HTMLInputElement>('#station-bar-p-coke')!;
    coke.checked = true;
    coke.dispatchEvent(new Event('change'));
    p.render();
    p.element.querySelector('#station-products-bar')!.dispatchEvent(new Event('submit'));
    p.render();

    expect(p.set).toEqual([{ stationId: 'bar', productIds: ['coke'] }]);
  });

  it('counts what goes to Default', () => {
    const p = page();
    expect(p.element.textContent).toContain('1 product(s) for sale have no station');
  });
});
