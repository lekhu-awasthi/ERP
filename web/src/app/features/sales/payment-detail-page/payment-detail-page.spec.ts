import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ContactsService } from '../../../core/contacts/contacts.service';
import { OpenAllocationTarget, PaymentGlPreviewRequest } from '../../../core/payments/payments.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { PaymentDetailPage } from './payment-detail-page';

/**
 * Phase 88 -- the Customer Payment form's allocation picker is this customer's open invoices in this payment's
 * currency, asked of the server whenever either changes, and its GL preview sends everything Approve's entry depends
 * on: the currency, the rate and the allocations.
 */
describe('PaymentDetailPage', () => {
  const open: OpenAllocationTarget = {
    targetDocumentType: 'Invoice', id: 'inv-1', code: 'INV-0001', date: '2026-10-01', dueDate: '2026-10-01', amount: 678,
    balance: 578, currencyCode: 'NPR',
  };

  function page() {
    const asked: unknown[][] = [];
    const previews: PaymentGlPreviewRequest[] = [];
    TestBed.configureTestingModule({
      imports: [PaymentDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ContactsService,
          useValue: { listAllContacts: () => of([{ id: 'acme', code: 'C-1', name: 'Acme Retail' }]) },
        },
        {
          provide: PaymentsService,
          useValue: {
            listOpenAllocationTargets: (...args: unknown[]) => {
              asked.push(args);
              return of(args[3] === 'NPR' ? [open] : []);
            },
            previewPaymentGlPosting: (_organizationId: string, request: PaymentGlPreviewRequest) => {
              previews.push(request);
              // A split: two lines on one account must both render.
              return of([
                { accountId: 'cash', debit: 1000, credit: 0 },
                { accountId: 'ar', debit: 0, credit: 600 },
                { accountId: 'ar', debit: 0, credit: 400 },
              ]);
            },
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ id: 'org-1' }) },
            paramMap: of(convertToParamMap({ id: 'org-1', paymentId: 'new' })),
            queryParamMap: of(convertToParamMap({})),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PaymentDetailPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();
    // The form's protected state, for the fields this spec does not exercise (account, amount, currency control).
    const form = fixture.componentInstance as unknown as Record<string, { set(value: unknown): void }> & {
      onCurrencyChange(code: string): void;
    };
    return { element, render, asked, previews, form };
  }

  afterEach(() => TestBed.resetTestingModule());

  it("asks for the chosen customer's open invoices in the payment's currency, again when the currency changes", () => {
    const p = page();
    expect(p.asked).toEqual([]);

    const customer = p.element.querySelector<HTMLSelectElement>('#payment-detail-page-customer')!;
    customer.value = 'acme';
    customer.dispatchEvent(new Event('change'));
    p.render();
    expect(p.asked).toEqual([['org-1', 'acme', 'Received', 'NPR']]);

    p.element.querySelector<HTMLButtonElement>('button.btn-outline-primary.btn-sm:not([disabled]) .bi-plus-lg')!
      .closest('button')!.click();
    p.render();
    const option = [...p.element.querySelectorAll<HTMLOptionElement>('select[aria-label="Invoice"] option')]
      .find((o) => o.value === 'inv-1')!;
    expect(option.textContent).toContain('INV-0001');
    expect(option.textContent).toContain('578.00 left');

    p.form.onCurrencyChange('USD');
    p.render();
    expect(p.asked[1]).toEqual(['org-1', 'acme', 'Received', 'USD']);
    expect([...p.element.querySelectorAll('select[aria-label="Invoice"] option')].map((o) => o.getAttribute('value')))
      .toEqual(['']);
  });

  it('previews with the currency, the rate and the allocations, and shows every line of a split', () => {
    const p = page();
    const customer = p.element.querySelector<HTMLSelectElement>('#payment-detail-page-customer')!;
    customer.value = 'acme';
    customer.dispatchEvent(new Event('change'));
    p.form['accountId'].set('cash');
    p.form['amount'].set(1000);
    p.form['allocations'].set([{ key: 1, targetDocumentId: 'inv-1', amount: 578 }, { key: 2, targetDocumentId: '', amount: 0 }]);
    p.render();

    [...p.element.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('Preview GL Posting'))!.click();
    p.render();

    expect(p.previews).toEqual([
      {
        accountId: 'cash', amount: 1000, direction: 'Received', currencyCode: 'NPR', exchangeRate: 1,
        allocations: [{ targetDocumentType: 'Invoice', targetDocumentId: 'inv-1', amount: 578 }],
      },
    ]);
    const credits = [...p.element.querySelectorAll('tbody tr')]
      .map((r) => r.querySelectorAll('td')[2]?.textContent?.trim())
      .filter((t) => t === '600.00' || t === '400.00');
    expect(credits).toEqual(['600.00', '400.00']);
  });
});
