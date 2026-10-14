import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { PrintingService } from '../../../core/printing/printing.service';
import { DebitNoteDetail, DebitNoteRequest } from '../../../core/purchasing/purchasing.models';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { InboxService } from '../../../core/workflow/inbox.service';
import { DebitNoteDetailPage } from './debit-note-detail-page';

/**
 * Phase 92 -- a debit note names the units it sends back. Phase 51's Approve read them and nothing wrote them, so
 * FIFO chose which phones went back to the supplier. The form's half: a serial line offers the scanner-friendly box
 * the credit note has (phase 91), reads the saved units back, and sends what was typed -- write, READ, and every
 * prefill between (phase 35a).
 */
describe('DebitNoteDetailPage — serial numbers', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const noteId = '33333333-3333-3333-3333-333333333333';

  const products = [
    { id: 'p-phone', code: 'P0004', name: 'Phone', type: 'Goods', serialTracking: true },
    { id: 'p-cable', code: 'P0005', name: 'Cable', type: 'Goods', serialTracking: false },
  ];

  function draft(): DebitNoteDetail {
    return {
      id: noteId,
      organizationId,
      contactId: 'c-1',
      code: 'DRAFT',
      date: '2026-10-14',
      reference: null,
      tdsTypeId: null,
      tdsAmount: 0,
      status: 'Draft',
      approvedByUserId: null,
      approvedAt: null,
      createdAt: '2026-10-14T03:00:00Z',
      referrerType: null,
      referrerId: null,
      discountPct: 0,
      locationId: null,
      currencyCode: 'NPR',
      exchangeRate: 1,
      warehouseId: 'w-1',
      glLines: null,
      lines: [
        {
          id: 'l-1', productId: 'p-phone', quantity: 3, rate: 8000, vatRate: 'ThirteenPercentVat', discountPct: 0,
          amount: 24000, vatAmount: 3120, unitId: null, unitName: null, conversionFactor: 1,
          serialNumbers: ['IMEI-1'],
        },
        {
          id: 'l-2', productId: 'p-cable', quantity: 1, rate: 100, vatRate: 'ThirteenPercentVat', discountPct: 0,
          amount: 100, vatAmount: 13, unitId: null, unitName: null, conversionFactor: 1, serialNumbers: [],
        },
      ],
    } as DebitNoteDetail;
  }

  function page() {
    const sent: DebitNoteRequest[] = [];
    const purchasingService = {
      getDebitNote: () => of(draft()),
      updateDebitNote: (_org: string, _id: string, request: DebitNoteRequest) => {
        sent.push(request);
        return of({ id: noteId, code: 'DRAFT', status: 'Draft' });
      },
    };

    TestBed.configureTestingModule({
      imports: [DebitNoteDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PurchasingService, useValue: purchasingService },
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        { provide: CatalogService, useValue: { listAllProducts: () => of(products), listUnitsOfMeasurement: () => of([]) } },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([]) } },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of([]),
            listCurrencies: () => of([{ code: 'NPR', name: 'Nepalese Rupee', symbol: 'Rs', isActive: true }]),
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
        { provide: PrintingService, useValue: {} },
        {
          provide: InboxService,
          useValue: {
            getPrefill: () => of(null),
            listDocuments: () => of({ items: [], totalCount: 0, page: 1, pageSize: 10 }),
            contentUrl: () => '',
            linkDocument: () => of(undefined),
          },
        },
        {
          provide: ConfigurationService,
          useValue: {
            listTdsTypes: () => of([]),
            listCustomFieldDefinitions: () => of([]),
            getCustomFieldValues: () => of([]),
            setCustomFieldValues: () => of(undefined),
            listReportingTagCategories: () => of([]),
            listReportingTagOptions: () => of([]),
            getTransactionReportingTags: () => of([]),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => organizationId }, queryParamMap: { get: () => null } },
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : noteId) }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(DebitNoteDetailPage);
    fixture.detectChanges();
    return { fixture, sent, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('offers the serial box on the serial line only, holding the units the draft saved', () => {
    const { el } = page();
    const boxes = el.querySelectorAll<HTMLInputElement>('input[aria-label^="Serial numbers returned of"]');

    expect(boxes.length).toBe(1);
    expect(boxes[0].getAttribute('aria-label')).toContain('Phone');
    expect(boxes[0].value).toBe('IMEI-1');
    const hint = el.querySelector<HTMLElement>('#' + boxes[0].getAttribute('aria-describedby'))!;
    expect(hint.textContent?.trim()).toBe('1 of 3 serial numbers');
  });

  it('takes a scan ended by Enter, keeps focus in the box, and saves exactly the units named', () => {
    const { fixture, sent, el } = page();
    const box = el.querySelector<HTMLInputElement>('input[aria-label^="Serial numbers returned of"]')!;
    box.focus();
    expect(document.activeElement).toBe(box);

    box.value = 'IMEI-1, IMEI-3';
    box.dispatchEvent(new Event('input'));
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    box.dispatchEvent(enter);
    fixture.detectChanges();

    expect(enter.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(box);
    expect(box.value).toBe('IMEI-1, IMEI-3, ');

    (fixture.componentInstance as unknown as { saveDraft: () => void }).saveDraft();

    expect(sent.length).toBe(1);
    expect(sent[0].lines.map((l) => l.serialNumbers)).toEqual([['IMEI-1', 'IMEI-3'], []]);
    // The units leave from a warehouse, which the form now reads back and sends (it had no control for it).
    expect(sent[0].warehouseId).toBe('w-1');
  });
});
