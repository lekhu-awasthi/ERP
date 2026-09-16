import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { CatalogService } from '../../../core/catalog/catalog.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { InventoryMasterRowDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { InventoryMasterReportPage } from './inventory-master-report-page';

/**
 * Phase 48, closing phase 44's own carried item #6 (*"No new Angular tests. The count stayed at 447
 * and that is a gap, not a claim."*).
 *
 * <p>What is pinned here is the thing phase 44's live read <b>overturned</b>: 26c's Decision D
 * excluded Opening Stock and Warehouse Transfer from the Txn Type filter, having predicted their
 * shape (two rows per transfer, every money column blank) exactly and treated that prediction as the
 * reason to leave them out. The reference product ships them anyway (Moonbeam, 2026-09-15). A
 * comment saying so does not survive a later tidy-up; this does.</p>
 */
describe('InventoryMasterReportPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function row(overrides: Partial<InventoryMasterRowDto> = {}): InventoryMasterRowDto {
    return {
      entryDate: '2026-05-04',
      contact: 'Acme Traders',
      documentType: 'Invoice',
      sourceDocumentId: '33333333-3333-3333-3333-333333333333',
      warehouse: 'Main',
      account: null,
      entryNo: 'INV0001',
      reference: null,
      productId: '22222222-2222-2222-2222-222222222222',
      product: 'Widget (P0001)',
      category: 'General',
      quantity: 4,
      unit: 'pc',
      rate: 100,
      amount: 400,
      itemDiscount: 0,
      transactionDiscount: 0,
      netAmount: 400,
      vatAmount: 52,
      totalAmount: 452,
      additionalCost: 0,
      ...overrides,
    };
  }

  function page(items: InventoryMasterRowDto[] = [row()]) {
    const asked: Record<string, unknown>[] = [];

    const reports = {
      getInventoryMaster: (...args: unknown[]): Observable<unknown> => {
        asked.push({ args });
        return of({ items, page: 1, pageSize: 50, totalCount: items.length });
      },
      exportInventoryMaster: (): Observable<Blob> => of(new Blob()),
    };

    TestBed.configureTestingModule({
      imports: [InventoryMasterReportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CatalogueReportsService, useValue: reports },
        { provide: CatalogService, useValue: { listAllProducts: () => of([]), listProductCategories: () => of([]) } },
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
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

    const fixture = TestBed.createComponent(InventoryMasterReportPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return { fixture, element, asked, text: () => element.textContent ?? '' };
  }

  afterEach(() => TestBed.resetTestingModule());

  /** The document-type options the Txn Type filter actually offers, in render order. */
  function txnTypeOptions(element: HTMLElement): string[] {
    const select = element.querySelector('#inventory-master-report-page-txn-type');
    expect(select, 'the Txn Type select was not found -- has its id changed?').toBeTruthy();
    return Array.from(select!.querySelectorAll('option'))
      .map((o) => (o as HTMLOptionElement).value)
      .filter((v) => v !== '');
  }

  it('offers Opening Stock and Warehouse Transfer, which 26c had excluded by prediction', () => {
    const { element } = page();

    const types = txnTypeOptions(element);
    expect(types).toContain('OpeningStock');
    expect(types).toContain('WarehouseTransfer');
  });

  it('lists Opening Stock first, as the live filter does', () => {
    const { element } = page();

    expect(txnTypeOptions(element)[0]).toBe('OpeningStock');
  });

  it('offers exactly the eight types the report covers, and no other DocumentType', () => {
    const { element } = page();

    // Deriving the expected list a second way rather than restating the component's array: these
    // are the eight the server's own query handles, so a ninth appearing here would be a filter
    // the user can pick and the report can never satisfy.
    expect(txnTypeOptions(element).sort()).toEqual(
      [
        'CreditNote',
        'DebitNote',
        'InventoryAdjustment',
        'Invoice',
        'OpeningStock',
        'ProductionJournal',
        'PurchaseBill',
        'WarehouseTransfer',
      ].sort(),
    );
  });

  it("renders a transfer's blank money columns as rows rather than dropping them", () => {
    // 26c predicted this shape and excluded the type because of it; the product ships it. A
    // transfer leg carries quantity and no money, and the row must still appear.
    const { element } = page([
      row({
        documentType: 'WarehouseTransfer',
        entryNo: 'WT0001',
        contact: null,
        rate: 0,
        amount: 0,
        netAmount: 0,
        vatAmount: 0,
        totalAmount: 0,
      }),
    ]);

    const cells = Array.from(element.querySelectorAll('tbody tr')).length;
    expect(cells, 'the transfer row was dropped').toBe(1);
    expect(element.textContent).toContain('WT0001');
  });
});
