import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { BillingLocation } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { DiscountScheme, DiscountSchemeRequest } from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { DiscountSchemeListPage } from './discount-scheme-list-page';

/** Phase 97 -- Configurations > Discount Schemes: a scheme saved with the rows its kind names, and edited in place. */
describe('DiscountSchemeListPage', () => {
  const festival: DiscountScheme = {
    id: 'ds-1', code: 'DASHAIN', name: 'Dashain', kind: 'Slab', startDate: '2026-10-01', endDate: '2026-10-31',
    allLocations: false, locationIds: ['loc-2'], isActive: true,
    lines: [{ productId: null, categoryId: null, minimumAmount: 5000, discountPct: 5 }],
  };

  function page(existing: DiscountScheme[] = []) {
    const created: DiscountSchemeRequest[] = [];
    const updated: { id: string; request: DiscountSchemeRequest }[] = [];
    TestBed.configureTestingModule({
      imports: [DiscountSchemeListPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: SalesService,
          useValue: {
            listDiscountSchemes: (): Observable<DiscountScheme[]> => of(existing),
            createDiscountScheme: (_o: string, request: DiscountSchemeRequest): Observable<DiscountScheme> => {
              created.push(request);
              return of(festival);
            },
            updateDiscountScheme: (_o: string, id: string, request: DiscountSchemeRequest): Observable<DiscountScheme> => {
              updated.push({ id, request });
              return of(festival);
            },
          },
        },
        {
          provide: CatalogService,
          useValue: {
            listAllProducts: (): Observable<Product[]> =>
              of([{ id: 'momo', code: 'P0001', name: 'Chicken Momo' } as unknown as Product]),
            listProductCategories: (): Observable<ProductCategory[]> =>
              of([{ id: 'food', name: 'Food' } as unknown as ProductCategory]),
          },
        },
        {
          provide: OrganizationsService,
          useValue: {
            listBillingLocations: (): Observable<BillingLocation[]> =>
              of([
                { id: 'loc-1', name: 'Head Office' } as unknown as BillingLocation,
                { id: 'loc-2', name: 'Lakeside' } as unknown as BillingLocation,
              ]),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'org-1' }) } } },
      ],
    });

    const fixture = TestBed.createComponent(DiscountSchemeListPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();
    const set = (selector: string, value: string, event = 'input') => {
      const control = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
      control.value = value;
      control.dispatchEvent(new Event(event));
      render();
    };
    const press = (label: string) => {
      [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(label))!.click();
      render();
    };
    return { element, created, updated, render, set, press };
  }

  it('saves an item-wise scheme for every location with the product and percent of each row', () => {
    const p = page();

    p.set('#discount-scheme-code', 'MOMO10');
    p.set('#discount-scheme-name', 'Momo week');
    p.set('[id^="discount-scheme-row-product-"]', 'momo', 'change');
    p.set('[id^="discount-scheme-row-pct-"]', '10');
    p.press('Add scheme');

    expect(p.created).toEqual([expect.objectContaining({
      code: 'MOMO10',
      name: 'Momo week',
      kind: 'ItemWise',
      allLocations: true,
      locationIds: [],
      lines: [{ productId: 'momo', categoryId: null, minimumAmount: null, discountPct: 10 }],
    })]);
  });

  it('opens a scheme for editing with its kind, locations and slabs, and sends it back by id', () => {
    const p = page([festival]);
    expect(p.element.textContent).toContain('from Rs 5000 5%');
    expect(p.element.textContent).toContain('Lakeside');

    p.press('Edit');
    expect(document.activeElement?.textContent).toContain('Edit Discount Scheme');
    expect(p.element.querySelector<HTMLInputElement>('#discount-scheme-location-loc-2')!.checked).toBe(true);
    expect(p.element.querySelector<HTMLInputElement>('[id^="discount-scheme-row-minimum-"]')!.value).toBe('5000');

    p.set('[id^="discount-scheme-row-pct-"]', '6');
    p.press('Update scheme');

    expect(p.updated[0].id).toBe('ds-1');
    expect(p.updated[0].request).toEqual(expect.objectContaining({
      kind: 'Slab',
      allLocations: false,
      locationIds: ['loc-2'],
      lines: [{ productId: null, categoryId: null, minimumAmount: 5000, discountPct: 6 }],
    }));
  });
});
