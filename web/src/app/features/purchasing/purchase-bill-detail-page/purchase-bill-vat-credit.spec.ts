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
 * Phase 99 -- a bill line's input-credit terms: what it was bought for (VAT Rule 40) and Rule 41's limit, prefilled
 * from the product, read back on a saved draft with the VAT the approval will not claim, and sent with the bill.
 */
describe('PurchaseBillDetailPage — VAT credit', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const billId = '22222222-2222-2222-2222-222222222222';

  const products = [
    { id: 'p-rent', code: 'P0001', name: 'Office rent', type: 'Service', purchasePrice: 1000, vatRate: 'ThirteenPercentVat', vatCreditCategory: 'Full' },
    { id: 'p-laptop', code: 'P0002', name: 'Laptop', type: 'Service', purchasePrice: 90000, vatRate: 'ThirteenPercentVat', vatCreditCategory: 'Computer' },
  ];

  function draft(): PurchaseBillDetail {
    return {
      id: billId, organizationId, contactId: 'c-1', warehouseId: 'w-1', locationId: null, code: 'DRAFT',
      date: '2026-10-20', dueDate: '2026-10-20', reference: null, supplierInvoiceReference: null, isImport: false,
      importCountry: null, importDate: null, importDocumentNo: null, tdsTypeId: null, tdsAmount: 0, status: 'Draft',
      approvedByUserId: null, approvedAt: null, createdAt: '2026-10-20T04:00:00Z', referrerType: null, referrerId: null,
      discountPct: 0, currencyCode: 'NPR', exchangeRate: 1, grandTotal: 500,
      lines: [{
        id: 'l-1', productId: 'p-rent', quantity: 1, rate: 1000, vatRate: 'ThirteenPercentVat', expenditureClassification: 'Others',
        discountPct: 0, amount: 1000, vatAmount: 130, batchNo: null, manufactureDate: null, expiryDate: null,
        serialNumbers: [], unitId: null, unitName: null, conversionFactor: 1,
        creditUse: 'Exempt', creditCategory: 'Full', disallowedVat: 130,
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

  it('reads a saved line’s terms back and names the VAT it will not claim', () => {
    const { el } = page();

    const use = el.querySelector<HTMLSelectElement>('select[aria-label="VAT credit use"]')!;
    const category = el.querySelector<HTMLSelectElement>('select[aria-label="VAT credit category"]')!;
    expect(use.selectedOptions[0].textContent).toBe('Exempt use');
    expect(category.selectedOptions[0].textContent).toBe('Full credit');
    expect(el.textContent).toContain('Not claimable: 130.00');
  });

  it('prefills a picked product’s Rule 41 category and sends both terms with the bill', () => {
    const { fixture, sent } = page();
    const component = fixture.componentInstance as unknown as {
      lines: () => { key: number; creditUse: string; creditCategory: string }[];
      onProductChange: (key: number, event: Event) => void;
      onCreditUseChange: (key: number, event: Event) => void;
      saveDraft: () => void;
    };
    const key = component.lines()[0].key;

    component.onProductChange(key, { target: { value: 'p-laptop' } } as unknown as Event);
    component.onCreditUseChange(key, { target: { value: 'Shared' } } as unknown as Event);
    fixture.detectChanges();
    component.saveDraft();

    expect(sent[0].lines[0]).toEqual(
      expect.objectContaining({ productId: 'p-laptop', creditUse: 'Shared', creditCategory: 'Computer' }));
  });
});
