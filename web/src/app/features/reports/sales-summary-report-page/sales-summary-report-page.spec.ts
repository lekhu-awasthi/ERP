import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { SalesSummaryReportDto, SalesSummaryRowDto } from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { SalesSummaryReportPage } from './sales-summary-report-page';

/**
 * Phase 48, closing phase 44's carried item #6 for this screen.
 *
 * <p>Phase 44 added <b>Group Wise location</b> here — the catalogue's only group-<i>by</i>-location
 * control. The property that matters is that it <b>composes</b> with the Billing Location filter
 * rather than replacing it: the filter chooses which locations are in scope, this chooses whether
 * the period's figures are split across them. Both must reach the server on the same request, which
 * is the thing a later edit could silently break.</p>
 */
describe('SalesSummaryReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  interface Ask {
    fiscalYear: number;
    mode: string;
    page: number;
    pageSize: number;
    locationId: string | null;
    groupWiseLocation: boolean;
  }

  function row(overrides: Partial<SalesSummaryRowDto> = {}): SalesSummaryRowDto {
    return {
      date: '2026-05-04',
      label: 'Jestha',
      subTotal: 1000,
      discount: 0,
      nonTaxableSales: 0,
      taxableSales: 1000,
      vat: 130,
      total: 1130,
      ...overrides,
    };
  }

  function page(rows: SalesSummaryRowDto[] = [row()], totalCount = rows.length) {
    const asks: Ask[] = [];

    const reports = {
      getSalesSummaryReport: (
        _org: string,
        fiscalYear: number,
        mode: string,
        pageNo: number,
        pageSize: number,
        locationId: string | null,
        groupWiseLocation: boolean,
      ): Observable<SalesSummaryReportDto> => {
        asks.push({ fiscalYear, mode, page: pageNo, pageSize, locationId, groupWiseLocation });
        return of({
          fiscalYear,
          mode: mode as SalesSummaryReportDto['mode'],
          fromDate: '2026-07-17',
          toDate: '2027-07-16',
          rows,
          page: pageNo,
          pageSize,
          totalCount,
        });
      },
      exportSalesSummaryReport: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [SalesSummaryReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: TradeReportsService, useValue: reports },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of([]),
            listBillingLocations: () => of([]),
            getBillingLocationSettings: () =>
              of({
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

    const fixture = TestBed.createComponent(SalesSummaryReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, asks, text: () => element.textContent ?? '' };
  }

  function groupWiseCheckbox(element: HTMLElement): HTMLInputElement {
    const box = element.querySelector<HTMLInputElement>('#sales-summary-report-page-group-wise-location');
    expect(box, 'the Group Wise location checkbox was not found -- has its id changed?').toBeTruthy();
    return box!;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('asks for no location split until Group Wise location is ticked', () => {
    const { asks } = page();

    expect(asks.length).toBe(1);
    expect(asks[0].groupWiseLocation).toBe(false);
  });

  it('sends the split flag on the next request when it is ticked', () => {
    const { element, fixture, asks } = page();

    const box = groupWiseCheckbox(element);
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(asks.length).toBe(2);
    expect(asks[1].groupWiseLocation).toBe(true);
  });

  it('returns to page 1 when it is ticked, because grouping multiplies the rows', () => {
    // A reader on page 3 of an ungrouped report would otherwise land somewhere unrelated once each
    // row becomes several -- the swept-in-filter rule (phase-35b), which every sibling filter on
    // this page already follows.
    // A totalCount past one page is what makes Next clickable at all.
    const { element, fixture, asks } = page([row()], 500);

    const next = Array.from(element.querySelectorAll('button')).find((b) => b.textContent?.trim().startsWith('Next'));
    expect(next, 'the Next button was not found').toBeTruthy();
    next!.click();
    fixture.detectChanges();
    expect(asks[asks.length - 1].page, 'the pager did not move').toBeGreaterThan(1);

    const box = groupWiseCheckbox(element);
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(asks[asks.length - 1].groupWiseLocation).toBe(true);
    expect(asks[asks.length - 1].page, 'ticking the split must reset the pager').toBe(1);
  });

  it('shows a Location column only once the split is on, and leaves it blank where a row has none', () => {
    // The column is part of the split, not of the report: without Group Wise location there is no
    // Location to show. Within a split, a document that carries none still produces a row -- phase
    // 44's DTO comment says so, and a blank is honest where a fabricated "HeadOffice" would not be.
    const { element, fixture, text } = page([
      row({ location: 'HeadOffice' }),
      row({ location: null, label: 'Ashar' }),
    ]);

    expect(text(), 'Location leaked in before the split was asked for').not.toContain('HeadOffice');

    const box = groupWiseCheckbox(element);
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(text()).toContain('HeadOffice');
    expect(text()).toContain('Ashar');
  });
});
