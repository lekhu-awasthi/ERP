import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ProductSerialReportDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ProductSerialReportPage } from './product-serial-report-page';

/**
 * Phase 51. The Status column is the one worth testing hardest: the catalogue showed a Status
 * filter that the product tab's three columns did not explain, and the kickoff warned against
 * inventing a lifecycle. Nothing was invented -- a serial is a FIFO layer of quantity one, so
 * In Stock / Issued is `QuantityRemaining` being 1 or 0. These assert it reads that way.
 */
describe('ProductSerialReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function report(overrides: Partial<ProductSerialReportDto> = {}): ProductSerialReportDto {
    return {
      fromDate: '2026-09-01',
      toDate: '2026-09-30',
      page: 1,
      pageSize: 50,
      totalCount: 2,
      inStockCount: 1,
      issuedCount: 1,
      items: [
        {
          serialNo: 'A1',
          productId: 'p1',
          productCode: 'PRD-0001',
          productName: 'Laptop',
          warehouseId: 'w1',
          warehouseName: 'Kathmandu',
          status: 'InStock',
          createdAt: '2026-09-02T04:15:00+00:00',
          unitCost: 100,
        },
        {
          serialNo: 'J9',
          productId: 'p1',
          productCode: 'PRD-0001',
          productName: 'Laptop',
          warehouseId: 'w1',
          warehouseName: 'Kathmandu',
          status: 'Issued',
          createdAt: '2026-09-02T04:15:00+00:00',
          unitCost: 250,
        },
      ],
      ...overrides,
    };
  }

  function page(dto: ProductSerialReportDto) {
    const reports = {
      getProductSerialReport: (): Observable<ProductSerialReportDto> => of(dto),
    };

    TestBed.configureTestingModule({
      imports: [ProductSerialReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(ProductSerialReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders one row per physical unit', () => {
    const { element } = page(report());

    const serials = Array.from(element.querySelectorAll('tbody tr td:first-child')).map((c) => c.textContent?.trim());
    expect(serials).toEqual(['A1', 'J9']);
  });

  it('renders the status as words, not as the enum member', () => {
    // `InStock` on a screen is a leak of the wire format.
    const { text } = page(report());

    expect(text()).toContain('In Stock');
    expect(text()).not.toContain('InStock');
  });

  it('shows the server-computed in-stock and issued counts', () => {
    const { text } = page(report({ inStockCount: 7, issuedCount: 3, totalCount: 10 }));

    expect(text()).toContain('7 in stock');
    expect(text()).toContain('3 issued');
  });

  it('says so plainly when the period holds no serials', () => {
    const { text } = page(report({ items: [], totalCount: 0, inStockCount: 0, issuedCount: 0 }));

    expect(text()).toContain('No serial numbers received in this period.');
  });

  it('offers exactly the three Status options the catalogue shows', () => {
    const { element } = page(report());

    const select = element.querySelector('#product-serial-report-page-status')!;
    const options = Array.from(select.querySelectorAll('option')).map((o) => o.getAttribute('value'));

    expect(options).toEqual(['All', 'InStock', 'Issued']);
  });
});
