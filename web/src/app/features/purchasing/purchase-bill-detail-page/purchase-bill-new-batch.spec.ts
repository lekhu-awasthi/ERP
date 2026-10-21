import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { PrintingService } from '../../../core/printing/printing.service';
import { PurchaseBillDetail, PurchaseBillRequest } from '../../../core/purchasing/purchasing.models';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { InboxService } from '../../../core/workflow/inbox.service';
import { PurchaseBillDetailPage } from './purchase-bill-detail-page';

/**
 * Phase 95 -- a batch this bill creates carries its expiry date and its own selling price, as the vendor's New Batch
 * form requires. The two fields appear only for a batch number the product does not have yet, and reach the request.
 */
describe('PurchaseBillDetailPage — a new batch', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const billId = '22222222-2222-2222-2222-222222222222';

  const products = [{ id: 'p-honey', code: 'P0094', name: 'Honey', type: 'Goods', batchTracking: true, purchasePrice: 100 }];

  function draft(): PurchaseBillDetail {
    return {
      id: billId, organizationId, contactId: 'c-1', warehouseId: 'w-1', locationId: null, code: 'DRAFT',
      date: '2026-10-20', dueDate: '2026-10-20', reference: null, supplierInvoiceReference: null, isImport: false,
      importCountry: null, importDate: null, importDocumentNo: null, tdsTypeId: null, tdsAmount: 0, status: 'Draft',
      approvedByUserId: null, approvedAt: null, createdAt: '2026-10-20T04:00:00Z', referrerType: null, referrerId: null,
      discountPct: 0, currencyCode: 'NPR', exchangeRate: 1, grandTotal: 500,
      lines: [{
        id: 'l-1', productId: 'p-honey', quantity: 5, rate: 100, vatRate: 'NoVat', expenditureClassification: 'Others',
        discountPct: 0, amount: 500, vatAmount: 0, batchNo: null, manufactureDate: null, expiryDate: null,
        serialNumbers: [], unitId: null, unitName: null, conversionFactor: 1,
      }],
      glLines: null, additionalCosts: [], isProductWiseAdditionalCost: false, additionalCostTotal: 0,
      capitalisedAdditionalCost: 0, additionalCostRoundingAdjustment: 0,
    } as unknown as PurchaseBillDetail;
  }

  function page() {
    const sent: PurchaseBillRequest[] = [];
    TestBed.configureTestingModule({
      imports: [PurchaseBillDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PurchasingService,
          useValue: {
            getPurchaseBill: (): Observable<PurchaseBillDetail> => of(draft()),
            updatePurchaseBill: (_o: string, _id: string, request: PurchaseBillRequest) => {
              sent.push(request);
              return of({ id: billId, code: 'DRAFT', status: 'Draft' });
            },
          },
        },
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        {
          provide: CatalogService,
          useValue: {
            listAllProducts: () => of(products),
            listUnitsOfMeasurement: () => of([]),
            // The product already has B1: naming B1 again creates nothing.
            listProductBatches: () => of([{ id: 'b-1', batchNo: 'B1', quantity: 2 }]),
          },
        },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([]) } },
        {
          provide: OrganizationsService,
          useValue: {
            listWarehouses: () => of([]),
            listCurrencies: () => of([{ code: 'NPR', name: 'Nepalese Rupee', symbol: 'Rs', isActive: true }]),
            listBillingLocations: () => of([]),
            getBillingLocationSettings: () =>
              of({
                locationScopeMode: 'SalesTransactionsOnly', locationWiseReportPermission: false,
                multipleLocationsEnabled: false, locationBearingDocumentTypes: [],
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
            listTdsTypes: () => of([]), listCreditTerms: () => of([]), listCostTerms: () => of([]),
            listCustomFieldDefinitions: () => of([]), getCustomFieldValues: () => of([]),
            setCustomFieldValues: () => of(undefined), listReportingTagCategories: () => of([]),
            listReportingTagOptions: () => of([]), getTransactionReportingTags: () => of([]),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => organizationId }, queryParamMap: { get: () => null } },
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : billId) }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PurchaseBillDetailPage);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const box = () => el.querySelector<HTMLInputElement>('input[aria-label="Item Batch"]')!;
    const type = (value: string) => {
      box().dispatchEvent(new Event('focus'));
      box().value = value;
      box().dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    return { fixture, el, sent, type };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('asks a new batch for its expiry and price, and an existing one for neither', () => {
    const { el, type } = page();

    type('B9');
    expect(el.textContent).toContain('New batch B9');
    expect(el.querySelector('label[for="bill-batch-price-' + keyOf(el) + '"]')?.textContent).toContain('Selling price');

    type('b1');
    expect(el.textContent).not.toContain('New batch');
  });

  it('sends the new batch’s expiry and selling price with the bill', () => {
    const { fixture, el, sent, type } = page();
    type('B9');
    const price = el.querySelector<HTMLInputElement>('input[id^="bill-batch-price-"]')!;
    price.value = '150';
    price.dispatchEvent(new Event('input'));
    const component = fixture.componentInstance as unknown as {
      onBatchExpiryChange: (key: number, value: string) => void;
      saveDraft: () => void;
    };
    component.onBatchExpiryChange(Number(price.id.replace('bill-batch-price-', '')), '2027-01-31');
    fixture.detectChanges();

    component.saveDraft();

    expect(sent[0].lines[0]).toEqual(
      expect.objectContaining({ batchNo: 'B9', expiryDate: '2027-01-31', batchSellingPrice: 150 }));
  });

  function keyOf(el: HTMLElement): string {
    return el.querySelector<HTMLInputElement>('input[id^="bill-batch-price-"]')!.id.replace('bill-batch-price-', '');
  }
});
