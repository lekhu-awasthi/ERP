import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { CatalogService } from '../../../core/catalog/catalog.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import {
  InventoryVarianceReportDto,
  InventoryVarianceRowDto,
} from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { InventoryVariancePage } from './inventory-variance-page';

/**
 * Phase 58. The two rows are the live read's own pair (Hamro Samaan, 2026-09-24): Book 3 against
 * Actual 7 is "Quantity To Be Shipped", Book 3 against Actual -3 is "Quantity To Be Received" with a
 * Difference of 6 -- the absolute gap, the sign living only in the remark.
 */
describe('InventoryVariancePage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<InventoryVarianceRowDto> = {}): InventoryVarianceRowDto {
    return {
      productId: '22222222-2222-2222-2222-222222222222',
      code: 'P0001',
      name: 'Widget',
      category: 'General',
      unit: 'BX',
      bookBalance: 3,
      actualBalance: 7,
      difference: 4,
      direction: 'ToBeShipped',
      ...overrides,
    };
  }

  function page(report: Partial<InventoryVarianceReportDto> = {}) {
    const calls: unknown[][] = [];
    const reports = {
      getInventoryVariance: (...args: unknown[]): Observable<InventoryVarianceReportDto> => {
        calls.push(args);
        return of({
          asOfDate: '2026-09-24',
          items: [
            row(),
            row({
              productId: '33333333-3333-3333-3333-333333333333',
              code: 'P0002',
              actualBalance: -3,
              difference: 6,
              direction: 'ToBeReceived',
            }),
          ],
          page: 1,
          pageSize: 50,
          totalCount: 2,
          mode: 'PhysicalMovement',
          ...report,
        });
      },
      exportInventoryVariance: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [InventoryVariancePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        {
          provide: CatalogService,
          useValue: { listProductCategories: () => of([]), listAllProducts: () => of([]) },
        },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of([]),
            listBillingLocations: () => of([]),
            getBillingLocationSettings: () => of({
              locationScopeMode: 'SalesTransactionsOnly',
              locationWiseReportPermission: false,
              multipleLocationsEnabled: false,
              locationBearingDocumentTypes: [],
            }),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(InventoryVariancePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, calls, text: () => element.textContent ?? '' };
  }

  function bodyRows(element: HTMLElement): string[][] {
    return Array.from(element.querySelectorAll('tbody tr')).map((tr) =>
      Array.from(tr.querySelectorAll('td')).map((td) => td.textContent?.trim() ?? ''));
  }

  afterEach(() => TestBed.resetTestingModule());

  it('names the direction in the remark and prints the absolute difference', () => {
    const { element } = page();

    const [shipped, received] = bodyRows(element);
    expect(shipped).toContain('Quantity To Be Shipped');
    expect(shipped).toContain('4.000 BX');
    expect(received).toContain('Quantity To Be Received');
    expect(received).toContain('-3.000 BX');
    expect(received).toContain('6.000 BX');
  });

  it('asks for today on the Nepal calendar with no category or product', () => {
    const { calls } = page();

    const [org, asOf, category, product] = calls[0];
    expect(org).toBe(organizationId);
    expect(asOf).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(category).toBeNull();
    expect(product).toBeNull();
  });

  it('says why Actual can be empty on an Accounting tenant', () => {
    const { text } = page({ mode: 'AccountingMovement' });

    expect(text()).toContain('tracks inventory by Accounting Movement');
  });

  it('says nothing about the mode on a Physical tenant', () => {
    const { text } = page();

    expect(text()).not.toContain('tracks inventory by Accounting Movement');
  });

  it('shows an agreeing state rather than a blank table', () => {
    const { text } = page({ items: [], totalCount: 0 });

    expect(text()).toContain('Book and actual balances agree');
  });
});
