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
 * Phase 93 -- a debit note names the batch it sends back. Phase 51's Approve consumed under the line's batch and
 * nothing wrote one, so FIFO chose which batch went back to the supplier; the vendor's form prefills the bill line's
 * batch and refuses a blank one. The form's half: a batch-tracked line offers the box, reads the saved batch back and
 * sends what was typed -- write, READ, and every prefill between (phase 35a).
 */
describe('DebitNoteDetailPage — batch', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const noteId = '33333333-3333-3333-3333-333333333333';

  const products = [
    { id: 'p-seed', code: 'P0006', name: 'Seed', type: 'Goods', batchTracking: true, serialTracking: false },
    { id: 'p-cable', code: 'P0005', name: 'Cable', type: 'Goods', batchTracking: false, serialTracking: false },
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
          id: 'l-1', productId: 'p-seed', quantity: 2, rate: 120, vatRate: 'ThirteenPercentVat', discountPct: 0,
          amount: 240, vatAmount: 31.2, unitId: null, unitName: null, conversionFactor: 1,
          serialNumbers: [], batchNo: 'B2',
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

  it('offers the batch box on the batch-tracked line only, holding the batch the draft saved', () => {
    const { el } = page();
    const boxes = el.querySelectorAll<HTMLInputElement>('input[aria-label^="Batch returned of"]');

    expect(boxes.length).toBe(1);
    expect(boxes[0].getAttribute('aria-label')).toContain('Seed');
    expect(boxes[0].value).toBe('B2');
  });

  it('saves the batch typed, focus staying in the box, and none for a line without batches', () => {
    const { fixture, sent, el } = page();
    const box = el.querySelector<HTMLInputElement>('input[aria-label^="Batch returned of"]')!;
    box.focus();
    expect(document.activeElement).toBe(box);

    box.value = ' B1 ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(document.activeElement).toBe(box);

    (fixture.componentInstance as unknown as { saveDraft: () => void }).saveDraft();

    expect(sent.length).toBe(1);
    expect(sent[0].lines.map((l) => l.batchNo)).toEqual(['B1', null]);
  });
});
