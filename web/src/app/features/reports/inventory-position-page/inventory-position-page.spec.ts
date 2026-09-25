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
          mode: 'AccountingMovement',
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
        {
          // Phase 35b -- the shared Billing Location filter reads both of these through
          // BillingLocationStore, so a double that answers only listWarehouses now throws inside the
          // filter's own computed() rather than failing an assertion.
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

  /**
   * Phase 48, closing phase 44's carried item #6 for this screen.
   *
   * Phase 44's live read (Moonbeam, 2026-09-15) settled a pair phase 36 could not tell apart on a
   * tenant it believed had one warehouse: <b>Display Warehouse in Column is not an alternative to
   * Group by Warehouse, it is a modifier of it</b>, `disabled` until that one is ticked. The server
   * refuses it alone with a 400 naming the field, so a screen that let the pair drift would show a
   * checked box above an error.
   */
  describe('the warehouse column pair (phase 44)', () => {
    function groupBox(element: HTMLElement): HTMLInputElement {
      return element.querySelector<HTMLInputElement>('#inventory-position-page-group-by-warehouse')!;
    }

    function columnBox(element: HTMLElement): HTMLInputElement {
      return element.querySelector<HTMLInputElement>('#inventory-position-page-display-warehouse-in-column')!;
    }

    function tick(fixture: { detectChanges(): void }, box: HTMLInputElement, checked: boolean): void {
      box.checked = checked;
      box.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    }

    it('disables the modifier until its parent is ticked', () => {
      const { element } = page();

      expect(groupBox(element), 'Group by Warehouse was not found').toBeTruthy();
      expect(columnBox(element).disabled, 'the modifier must start disabled').toBe(true);
    });

    it('enables the modifier once the parent is ticked', () => {
      const { element, fixture } = page();

      tick(fixture, groupBox(element), true);

      expect(columnBox(element).disabled).toBe(false);
    });

    it('clears the modifier when the parent is unticked', () => {
      // Otherwise the next request carries a flag the server refuses on its own, and the screen
      // shows a checked box above a 400.
      const { element, fixture } = page();

      tick(fixture, groupBox(element), true);
      tick(fixture, columnBox(element), true);
      expect(columnBox(element).checked).toBe(true);

      tick(fixture, groupBox(element), false);

      expect(columnBox(element).checked, 'unticking the parent must clear the modifier').toBe(false);
      expect(columnBox(element).disabled).toBe(true);
    });
  });

  /**
   * Phase 58. The physical ledger carries no cost, so its rows arrive with rate and amount zero;
   * printing them would read as stock worth nothing. The columns go, and the select shows the
   * ledger the server says it read -- not the one the screen assumed.
   */
  describe('the Mode of Inventory Tracking (phase 58)', () => {
    function headers(element: HTMLElement): string[] {
      return Array.from(element.querySelectorAll('thead th')).map((h) => h.textContent?.trim() ?? '');
    }

    it('shows Rate and Amount on the accounting ledger', () => {
      const { element } = page();

      expect(headers(element)).toContain('Rate');
      expect(headers(element)).toContain('Amount');
    });

    it('hides Rate and Amount on the physical ledger, and the total amount with them', () => {
      const { element, text } = page({ mode: 'PhysicalMovement', items: [row({ rate: 0, amount: 0 })] });

      expect(headers(element)).not.toContain('Rate');
      expect(headers(element)).not.toContain('Amount');
      expect(text()).not.toContain('8,750.00');
      expect(text()).toContain('940.000');
    });

    it('selects the mode the server answered with', () => {
      const { element } = page({ mode: 'PhysicalMovement' });

      const select = element.querySelector<HTMLSelectElement>('#inventory-position-page-mode')!;
      expect(select.value).toBe('PhysicalMovement');
    });
  });

  it('shows an empty state rather than a blank table', () => {
    const { text } = page({ items: [], totalCount: 0, totalQuantity: 0, totalAmount: 0 });

    expect(text()).toContain('No stock matches these filters');
  });
});
