import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { CatalogService } from '../../../core/catalog/catalog.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import {
  InventoryPositionReportDto,
  InventoryPositionRowDto,
} from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { InventoryPositionPage } from './inventory-position-page';

/**
 * Phase 26c. Two behaviours are pinned: a negative-balance row shows no rate and no amount (there is
 * no cost to carry for stock that is not there -- the live report prints "-" in both cells), and the
 * footer totals are the server's full-set figures rather than a reduce over the loaded page
 * (phase-16c bug #1, which is why the stub's totals are deliberately larger than its one row).
 */
describe('InventoryPositionPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<InventoryPositionRowDto> = {}): InventoryPositionRowDto {
    return {
      productId: '22222222-2222-2222-2222-222222222222',
      product: 'Widget (P0001)',
      category: 'General',
      quantity: 120,
      unit: 'pc',
      rate: 10.833,
      amount: 1300,
      ...overrides,
    };
  }

  function page(report: Partial<InventoryPositionReportDto> = {}) {
    const reports = {
      getInventoryPosition: (): Observable<InventoryPositionReportDto> =>
        of({
          fromDate: '2026-05-01',
          toDate: '2026-05-31',
          items: [row()],
          page: 1,
          pageSize: 50,
          totalCount: 9,
          // Deliberately larger than the single row: these are the full-set totals.
          totalQuantity: 940,
          totalAmount: 8750,
          ...report,
        }),
      exportInventoryPosition: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [InventoryPositionPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        {
          provide: CatalogService,
          useValue: { listProductCategories: () => of([]), listAllProducts: () => of([]) },
        },
        { provide: OrganizationsService, useValue: { listWarehouses: () => of([]) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(InventoryPositionPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows rate and amount for a positive balance', () => {
    const { element } = page();

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells).toContain('10.833');
    expect(cells).toContain('1,300.00');
  });

  it('shows no rate and no amount for a negative balance', () => {
    const { element } = page({ items: [row({ quantity: -3, rate: 0, amount: 0 })] });

    const cells = Array.from(element.querySelectorAll('tbody tr td')).map((c) => c.textContent?.trim());
    expect(cells).toContain('-3.000');
    // Both the Rate and Amount cells fall back to an em dash.
    expect(cells.filter((c) => c === '—').length).toBeGreaterThanOrEqual(2);
  });

  it('shows the server-computed totals over the full filtered set, not the page', () => {
    const { text } = page();

    expect(text()).toContain('8,750.00');
    expect(text()).toContain('940.000');
  });

  it('shows an empty state rather than a blank table', () => {
    const { text } = page({ items: [], totalCount: 0, totalQuantity: 0, totalAmount: 0 });

    expect(text()).toContain('No stock matches these filters');
  });
});
