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
import { PendingTemplateStore } from '../../../core/sales/pending-template.store';
import { CreditableInvoice, CreditNoteDetail, CreditNoteRequest } from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { CreditNoteDetailPage } from './credit-note-detail-page';

/**
 * Phase 69 -- the credit note names the tax invoice it relates to (VAT Rules Rule 20(1)(e)). The server
 * enforces every rule; these pin that the form offers the two ways to name one, sends what was chosen,
 * reads it back, and shows it on a saved note -- the write, read and prefill phase 35a owes a new field.
 */
describe('CreditNoteDetailPage — the invoice it relates to', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const noteId = '22222222-2222-2222-2222-222222222222';

  const invoices: CreditableInvoice[] = [
    {
      id: 'inv-2', code: 'INV0002', date: '2026-09-05', locationId: null, currencyCode: 'USD', exchangeRate: 133,
      grandTotal: 1130, creditedTotal: 0, remainingTotal: 1130,
    },
    {
      id: 'inv-1', code: 'INV0001', date: '2026-09-01', locationId: null, currencyCode: 'NPR', exchangeRate: 1,
      grandTotal: 2260, creditedTotal: 2260, remainingTotal: 0,
    },
  ];

  function detail(overrides: Partial<CreditNoteDetail> = {}): CreditNoteDetail {
    return {
      id: noteId, organizationId, contactId: 'c-1', code: 'CN0001', date: '2026-09-10', reference: null,
      status: 'Approved', approvedByUserId: null, approvedAt: '2026-09-10T05:00:00Z', createdAt: '2026-09-10T04:00:00Z',
      referrerType: null, referrerId: null, discountPct: 0, terms: null, locationId: null,
      againstInvoiceId: 'inv-1', againstInvoiceNumber: null, againstInvoiceDate: null, reason: 'Price cut',
      currencyCode: 'NPR', exchangeRate: 1,
      lines: [{
        id: 'l-1', productId: 'p-1', quantity: 1, rate: 100, vatRate: 'NoVat', discountPct: 0, amount: 100,
        vatAmount: 0, unitId: null, unitName: null, conversionFactor: 1, serviceChargeAmount: 0,
      }],
      glLines: null, posRefund: null, printCount: 0,
      relatedInvoice: { id: 'inv-1', code: 'INV0001', date: '2026-09-01', grandTotal: 2260, currencyCode: 'NPR' },
      ...overrides,
    };
  }

  function page(routeNoteId: string, note: CreditNoteDetail | null = null, seed?: () => void) {
    const sent: { create: CreditNoteRequest[]; pickerCalls: unknown[][] } = { create: [], pickerCalls: [] };
    const salesService = {
      getCreditNote: (): Observable<CreditNoteDetail> => of(note!),
      listCreditableInvoices: (...args: unknown[]) => {
        sent.pickerCalls.push(args);
        return of({ items: invoices, page: 1, pageSize: 20, totalCount: invoices.length });
      },
      createCreditNote: (_org: string, request: CreditNoteRequest) => {
        sent.create.push(request);
        return of({ id: noteId, code: 'DRAFT', status: 'Draft' });
      },
    };

    TestBed.configureTestingModule({
      imports: [CreditNoteDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Save navigates to the saved note; a componentless catch-all lets that navigation complete.
        provideRouter([{ path: '**', children: [] }]),
        { provide: SalesService, useValue: salesService },
        {
          provide: ContactsService,
          useValue: { listAllContacts: () => of([{ id: 'c-1', code: 'C0001', name: 'Acme Traders' }]) },
        },
        {
          provide: CatalogService,
          useValue: {
            listAllProducts: () => of([{ id: 'p-1', code: 'P0001', name: 'Consulting', type: 'Service', sellingPrice: 100, vatRate: 'NoVat' }]),
            listUnitsOfMeasurement: () => of([]),
            suggestProductRate: () => of({ rate: 100, vatRate: 'NoVat' }),
          },
        },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([]) } },
        {
          provide: OrganizationsService,
          useValue: {
            listCurrencies: () => of([
              { code: 'NPR', name: 'Nepalese Rupee', symbol: 'Rs', isActive: true },
              { code: 'USD', name: 'US Dollar', symbol: '$', isActive: true },
            ]),
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
          provide: ConfigurationService,
          useValue: {
            listCustomTemplates: () => of([]),
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
            paramMap: of({ get: (key: string) => (key === 'id' ? organizationId : routeNoteId) }),
          },
        },
      ],
    });

    seed?.();
    const fixture = TestBed.createComponent(CreditNoteDetailPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance as unknown as {
      saveDraft: () => void;
      currencyCode: () => string;
      exchangeRate: () => number;
    };

    return { fixture, root, component, sent, text: () => root.textContent ?? '' };
  }

  function choose(root: HTMLElement, selector: string, value: string): void {
    const select = root.querySelector<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists the chosen customer’s invoices with what is left, and a fully credited one cannot be picked', () => {
    const { fixture, root, sent } = page('new');

    choose(root, '#credit-note-detail-page-customer', 'c-1');
    fixture.detectChanges();

    expect(sent.pickerCalls.at(-1)?.[1]).toBe('c-1');
    const options = Array.from(root.querySelectorAll<HTMLOptionElement>('#credit-note-detail-page-invoice option'));
    expect(options.map((o) => o.value)).toEqual(['', 'inv-2', 'inv-1']);
    expect(options[1].textContent).toContain('1,130.00 left');
    expect(options[2].textContent).toContain('fully credited');
    expect(options[2].disabled).toBe(true);
  });

  it('picking an invoice takes its currency and rate, and Save sends it with the reason', () => {
    const { fixture, root, component, sent } = page('new');
    choose(root, '#credit-note-detail-page-customer', 'c-1');
    fixture.detectChanges();
    choose(root, 'table select', 'p-1');

    choose(root, '#credit-note-detail-page-invoice', 'inv-2');
    const reason = root.querySelector<HTMLTextAreaElement>('#credit-note-detail-page-reason')!;
    reason.value = 'Agreed a lower price';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(component.currencyCode()).toBe('USD');
    expect(component.exchangeRate()).toBe(133);

    component.saveDraft();
    expect(sent.create).toEqual([expect.objectContaining({
      againstInvoiceId: 'inv-2', againstInvoiceNumber: null, againstInvoiceDate: null,
      reason: 'Agreed a lower price', currencyCode: 'USD', exchangeRate: 133,
    })]);
  });

  it('sends a typed invoice as its number and date, and refuses one without the other', () => {
    const { fixture, root, component, sent } = page('new');
    choose(root, '#credit-note-detail-page-customer', 'c-1');
    choose(root, 'table select', 'p-1');
    root.querySelector<HTMLInputElement>('#credit-note-invoice-mode-typed')!.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const number = root.querySelector<HTMLInputElement>('#credit-note-detail-page-invoice-number')!;
    number.value = 'OLD-0042';
    number.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    component.saveDraft();
    fixture.detectChanges();
    expect(sent.create.length).toBe(0);
    expect(number.getAttribute('aria-invalid')).toBe('true');

    (component as unknown as { typedInvoiceDate: { set: (v: string) => void } }).typedInvoiceDate.set('2025-12-01');
    component.saveDraft();

    expect(sent.create.length).toBe(1);
    expect(sent.create[0]).toEqual(expect.objectContaining({
      againstInvoiceId: null, againstInvoiceNumber: 'OLD-0042', againstInvoiceDate: '2025-12-01',
    }));
  });

  it('shows a saved note’s invoice as a link with its date, and its reason', () => {
    const { root, text } = page(noteId, detail());

    const link = root.querySelector<HTMLAnchorElement>(`a[href$="/sales/invoices/inv-1"]`);
    expect(link?.textContent?.trim()).toBe('INV0001');
    expect(text()).toContain('dated 01-09-2026');
    expect(root.querySelector<HTMLTextAreaElement>('#credit-note-detail-page-reason')!.value).toBe('Price cut');
    expect(root.querySelector('#credit-note-detail-page-invoice')).toBeNull();
  });

  it('shows a typed invoice as issued before this system', () => {
    const { text } = page(
      noteId,
      detail({ againstInvoiceId: null, relatedInvoice: null, againstInvoiceNumber: 'OLD-0042', againstInvoiceDate: '2025-12-01' }),
    );

    expect(text()).toContain('OLD-0042 dated 01-12-2025');
    expect(text()).toContain('issued before this system');
  });

  it('reads a draft adjustment back into the picker without counting the draft against its invoice', () => {
    const { root, sent } = page(noteId, detail({ status: 'Draft', approvedAt: null }));

    expect(root.querySelector<HTMLSelectElement>('#credit-note-detail-page-invoice')!.value).toBe('inv-1');
    expect(sent.pickerCalls.at(-1)?.[4]).toBe(noteId);
  });

  it('a conversion takes the invoice’s currency and rate from the template', () => {
    // The store is provided in root: seed it after the module is configured and before the page reads it.
    const { component, root } = page('new', null, () =>
      TestBed.inject(PendingTemplateStore).setCreditNoteTemplate({
        contactId: 'c-1', date: '2026-09-10', reference: 'From Invoice INV0002', referrerType: 'Invoice',
        referrerId: 'inv-2', discountPct: 0, lines: [], locationId: null, terms: null,
        currencyCode: 'USD', exchangeRate: 133,
      }));

    expect(component.currencyCode()).toBe('USD');
    expect(component.exchangeRate()).toBe(133);
    expect(root.querySelector('#credit-note-detail-page-invoice')).toBeNull();
  });
});
