import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { NetTradingAssetsDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { NetTradingAssetsPage } from './net-trading-assets-page';

/**
 * Phase 26c. The Compare column must be labelled with the date it actually used, never the word
 * "prior" -- phase-26a's rule, and the reason the server echoes `compareAsOfDate` at all.
 */
describe('NetTradingAssetsPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function report(overrides: Partial<NetTradingAssetsDto> = {}): NetTradingAssetsDto {
    return {
      fromDate: '2026-05-01',
      toDate: '2026-05-31',
      excludeAdvance: false,
      compareAsOfDate: null,
      rows: [
        {
          particulars: 'Receivables',
          balance: 1000,
          compareBalance: null,
          children: [
            { particulars: 'Receivables from Customers', balance: 900, compareBalance: null, children: [] },
            { particulars: 'Advance to Suppliers', balance: 100, compareBalance: null, children: [] },
          ],
        },
        { particulars: 'Net Trading Assets', balance: 1600, compareBalance: null, children: [] },
      ],
      ...overrides,
    };
  }

  function page(dto: NetTradingAssetsDto) {
    const reports = {
      getNetTradingAssets: (): Observable<NetTradingAssetsDto> => of(dto),
      exportNetTradingAssets: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [NetTradingAssetsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(NetTradingAssetsPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders each grouped row above its own children', () => {
    const { element } = page(report());

    const labels = Array.from(element.querySelectorAll('tbody tr td:first-child')).map((c) => c.textContent?.trim());
    expect(labels).toEqual([
      'Receivables',
      'Receivables from Customers',
      'Advance to Suppliers',
      'Net Trading Assets',
    ]);
  });

  it('shows only two columns when Compare is off', () => {
    const { element } = page(report());

    expect(element.querySelectorAll('thead th').length).toBe(2);
  });

  it('labels the Compare column with the date it used, never the word "prior"', () => {
    const { element, text } = page(
      report({
        compareAsOfDate: '2025-05-31',
        rows: [{ particulars: 'Net Trading Assets', balance: 1600, compareBalance: 1250, children: [] }],
      }),
    );

    expect(element.querySelectorAll('thead th').length).toBe(3);
    expect(text()).toContain('Balance as at');
    expect(text()).not.toContain('Prior');
    expect(text()).toContain('1,250.00');
  });

  it('points at the reports that hold the detail behind these totals', () => {
    const { text } = page(report());

    expect(text()).toContain('Customer Receivable Summary');
    expect(text()).toContain('Inventory Position');
  });
});
