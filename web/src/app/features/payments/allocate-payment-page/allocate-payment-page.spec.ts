import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ContactsService } from '../../../core/contacts/contacts.service';
import { PaymentsService } from '../../../core/payments/payments.service';
import { AllocatablePaymentDto } from '../../../core/payments/payments.models';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { SalesService } from '../../../core/sales/sales.service';
import { AllocatePaymentPage } from './allocate-payment-page';

/**
 * Phase 87 -- the Allocate screen's Apply, Cancel and Confirm each remove themselves. Each hands keyboard focus on: Apply
 * to the document picker it opens, Cancel back to that row's Apply, Confirm to the page heading above the outcome.
 */
describe('AllocatePaymentPage', () => {
  const receipt: AllocatablePaymentDto = {
    sourceType: 'Payment', id: 'pay-1', parentDocumentId: null, code: '0001', date: '2026-10-12', contactId: 'acme',
    contactName: 'Acme Retail', amount: 400, allocated: 235.6, balance: 164.4, currencyCode: 'NPR',
  };

  function page() {
    const applied: unknown[][] = [];
    TestBed.configureTestingModule({
      imports: [AllocatePaymentPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        {
          provide: SalesService,
          useValue: {
            listInvoices: () =>
              of({ page: 1, pageSize: 200, totalCount: 1, items: [{ id: 'inv-y', code: '0002', contactId: 'acme', currencyCode: 'NPR' }] }),
          },
        },
        { provide: PurchasingService, useValue: { listAllPurchaseBills: () => of([]) } },
        {
          provide: PaymentsService,
          useValue: {
            listAllocatablePayments: () => of({ page: 1, pageSize: 50, totalCount: 1, items: [receipt] }),
            applyPaymentAllocation: (...args: unknown[]) => {
              applied.push(args);
              return of({});
            },
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'org-1' }, data: { direction: 'Received' } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(AllocatePaymentPage);
    const element = fixture.nativeElement as HTMLElement;
    document.body.appendChild(element);
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    /** As a keyboard user does: focus the button, check it has focus, then press it. */
    const press = (button: HTMLButtonElement) => {
      button.focus();
      expect(document.activeElement).toBe(button);
      button.click();
      render();
    };
    const button = (text: string) =>
      [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(text))!;

    return { element, render, press, button, applied };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('hands focus to the document picker when Apply opens the form, and back to Apply when it is cancelled', () => {
    const p = page();

    p.press(p.button('Apply'));
    expect(p.element.querySelector('#allocate-apply-pay-1')).toBeNull();
    expect(document.activeElement?.id).toBe('allocate-payment-page-select-2');

    p.press(p.button('Cancel'));
    expect(document.activeElement?.id).toBe('allocate-apply-pay-1');
  });

  it('sends the chosen document and amount, and hands focus to the heading once Confirm has gone', () => {
    const p = page();
    p.press(p.button('Apply'));

    const picker = p.element.querySelector<HTMLSelectElement>('#allocate-payment-page-select-2')!;
    picker.value = 'inv-y';
    picker.dispatchEvent(new Event('change'));
    const amount = p.element.querySelector<HTMLInputElement>('#allocate-payment-page-amount')!;
    amount.value = '35.6';
    amount.dispatchEvent(new Event('change'));
    p.render();

    p.press(p.button('Confirm'));

    expect(p.applied).toEqual([['org-1', 'Payment', 'pay-1', null, 'Invoice', 'inv-y', 35.6]]);
    expect(document.activeElement?.id).toBe('allocate-payment-page-heading');
    expect(p.element.textContent).toContain('Applied 35.60 from 0001.');
  });
});
