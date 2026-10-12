import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ContactsService } from '../../../core/contacts/contacts.service';
import { PaymentsService } from '../../../core/payments/payments.service';
import { AllocatablePaymentDto, OpenAllocationTarget } from '../../../core/payments/payments.models';
import { AllocatePaymentPage } from './allocate-payment-page';

/**
 * Phase 87 -- the Allocate screen's Apply, Cancel and Confirm each remove themselves. Each hands keyboard focus on: Apply
 * to the document picker it opens, Cancel back to that row's Apply, Confirm to the page heading above the outcome.
 *
 * Phase 88 -- the picker lists the row's contact's open documents in its currency, asked of the server when Apply
 * opens the form, and choosing one fills Amount with as much as both sides allow.
 */
describe('AllocatePaymentPage', () => {
  const receipt: AllocatablePaymentDto = {
    sourceType: 'Payment', id: 'pay-1', parentDocumentId: null, code: '0001', date: '2026-10-12', contactId: 'acme',
    contactName: 'Acme Retail', amount: 400, allocated: 235.6, balance: 164.4, currencyCode: 'NPR',
  };

  const open: OpenAllocationTarget = {
    targetDocumentType: 'Invoice', id: 'inv-y', code: '0002', date: '2026-10-01', dueDate: '2026-10-01', amount: 678,
    balance: 100, currencyCode: 'NPR',
  };

  function page() {
    const applied: unknown[][] = [];
    const asked: unknown[][] = [];
    TestBed.configureTestingModule({
      imports: [AllocatePaymentPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ContactsService, useValue: { listAllContacts: () => of([]) } },
        {
          provide: PaymentsService,
          useValue: {
            listAllocatablePayments: () => of({ page: 1, pageSize: 50, totalCount: 1, items: [receipt] }),
            listOpenAllocationTargets: (...args: unknown[]) => {
              asked.push(args);
              return of([open]);
            },
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

    return { element, render, press, button, applied, asked };
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

  it("asks for the row's contact's open documents in its currency, and a chosen one fills Amount with what both allow", () => {
    const p = page();
    expect(p.asked).toEqual([]);
    p.press(p.button('Apply'));

    expect(p.asked).toEqual([['org-1', 'acme', 'Received', 'NPR']]);
    const picker = p.element.querySelector<HTMLSelectElement>('#allocate-payment-page-select-2')!;
    const option = [...picker.options].find((o) => o.value === 'inv-y')!;
    expect(option.textContent).toContain('0002');
    expect(option.textContent).toContain('100.00 left');

    // The receipt has 164.40 left and the invoice 100: Amount takes 100, and Confirm sends it.
    picker.value = 'inv-y';
    picker.dispatchEvent(new Event('change'));
    p.render();
    expect(p.element.querySelector<HTMLInputElement>('#allocate-payment-page-amount')!.value).toBe('100');

    p.press(p.button('Confirm'));
    expect(p.applied).toEqual([['org-1', 'Payment', 'pay-1', null, 'Invoice', 'inv-y', 100]]);
  });
});
