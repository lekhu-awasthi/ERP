import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ProductBatchReportDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ProductBatchReportPage } from './product-batch-report-page';

/**
 * Phase 51. These assertions are about *this* screen's contract, not about the vendor's -- the
 * vendor's columns were never read (Decision A), so there is nothing to compare against and
 * pretending otherwise would be the worse outcome.
 */
describe('ProductBatchReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function report(overrides: Partial<ProductBatchReportDto> = {}): ProductBatchReportDto {
    return {
      fromDate: '2026-09-01',
      toDate: '2026-09-30',
      page: 1,
      pageSize: 50,
      totalCount: 2,
      totalQuantity: 30,
      items: [
        {
          batchId: 'b1',
          batchNo: 'BATCH123',
          productId: 'p1',
          productCode: 'PRD-0001',
          productName: 'Paracetamol',
          manufactureDate: '2026-09-01',
          expiryDate: '2026-09-03',
          warehouseId: null,
          warehouseName: null,
          quantity: 20,
          unitName: 'CTN',
        },
        {
          batchId: 'b2',
          batchNo: 'BATCH999',
          productId: 'p1',
          productCode: 'PRD-0001',
          productName: 'Paracetamol',
          manufactureDate: null,
          expiryDate: null,
          warehouseId: null,
          warehouseName: null,
          quantity: 10,
          unitName: 'CTN',
        },
      ],
      ...overrides,
    };
  }

  function page(dto: ProductBatchReportDto) {
    const reports = {
      getProductBatchReport: (): Observable<ProductBatchReportDto> => of(dto),
    };

    TestBed.configureTestingModule({
      imports: [ProductBatchReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(ProductBatchReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders one row per batch with its number and quantity', () => {
    const { element } = page(report());

    const numbers = Array.from(element.querySelectorAll('tbody tr td:first-child')).map((c) => c.textContent?.trim());
    expect(numbers).toEqual(['BATCH123', 'BATCH999']);
  });

  it('renders a batch with no dates as an em dash rather than a blank cell', () => {
    // Both dates are nullable on purpose -- the read establishes neither as required, and a lot
    // number with no expiry is ordinary. An empty cell reads as a rendering bug.
    const { element } = page(report());

    const secondRow = element.querySelectorAll('tbody tr')[1];
    const cells = Array.from(secondRow.querySelectorAll('td')).map((c) => c.textContent?.trim());

    expect(cells[2]).toBe('—');
    expect(cells[3]).toBe('—');
  });

  it('shows the server-computed total, not a reduce over the page', () => {
    // Phase-16c bug #1. The page holds 30 here too, so the test makes the server disagree with the
    // page on purpose -- otherwise it would pass for a client-side sum as well, and assert nothing.
    const { text } = page(report({ totalQuantity: 999, totalCount: 40 }));

    expect(text()).toContain('999');
  });

  it('hides the Warehouse column unless Group By is Warehouse', () => {
    const { element } = page(report());

    const headers = Array.from(element.querySelectorAll('thead th')).map((h) => h.textContent?.trim());
    expect(headers).not.toContain('Warehouse');
  });

  it('says so plainly when the period holds no batches', () => {
    const { text } = page(report({ items: [], totalCount: 0, totalQuantity: 0 }));

    expect(text()).toContain('No batches received in this period.');
  });
});
