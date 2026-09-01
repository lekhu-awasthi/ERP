import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import {
  ApprovePaymentResult,
  CreatePaymentResult,
  PaymentRequest,
} from '../../../core/payments/payments.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { QuickPaymentPage } from './quick-payment-page';

/**
 * Phase 22 changed this screen from one-shot create-and-approve to <b>Draft, then Approve</b> — see
 * `docs/phase-17-status.md`'s addendum for why. These assert the split itself, because the failure
 * mode it exists to prevent is silent: a regression back to auto-approve would post to the General
 * Ledger on one click from AI-suggested values, and every screenshot of the page would still look
 * right.
 */
describe('QuickPaymentPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const contactId = '22222222-2222-2222-2222-222222222222';
  const accountId = '33333333-3333-3333-3333-333333333333';

  function page(payments: PaymentsServiceStub): {
    fixture: ComponentFixture<QuickPaymentPage>;
    text: () => string;
    element: () => HTMLElement;
    fillAndSubmit: () => void;
    click: (label: string) => void;
  } {
    TestBed.configureTestingModule({
      imports: [QuickPaymentPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PaymentsService, useValue: payments },
        { provide: ContactsService, useValue: { listAllContacts: () => of([{ id: contactId, code: '0001', name: 'Acme' }]) } },
        {
          provide: AccountingService,
          useValue: { listAllAccounts: () => of([{ id: accountId, code: '1000', name: 'Cash', kind: 'Cash' }]) },
        },
        { provide: ConfigurationService, useValue: { listPaymentModes: () => of([]) } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => organizationId },
              queryParamMap: { get: () => null },
              data: { direction: 'Received' },
            },
            data: { direction: 'Received' },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(QuickPaymentPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance as unknown as {
      form: { patchValue: (v: Record<string, unknown>) => void };
      saveDraft: () => void;
    };

    return {
      fixture,
      element,
      text: () => element().textContent ?? '',
      fillAndSubmit: () => {
        component.form.patchValue({ contactId, accountId, amount: 5000 });
        component.saveDraft();
        fixture.detectChanges();
      },
      click: (label: string) => {
        const button = [...element().querySelectorAll('button')].find((b) =>
          (b.textContent ?? '').trim().startsWith(label),
        );
        button?.click();
        fixture.detectChanges();
      },
    };
  }

  it('offers Save Draft, not Approve, on a fresh form', () => {
    const { text } = page(new PaymentsServiceStub());
    expect(text()).toContain('Save Draft');
    expect(text()).toContain('Nothing is posted until it is approved');
    expect(text()).not.toContain('Approve Receipt');
  });

  it('creates a Draft without approving it', () => {
    const payments = new PaymentsServiceStub();
    const { text, fillAndSubmit } = page(payments);

    fillAndSubmit();

    expect(payments.created.length).toBe(1);
    expect(payments.approvedIds).toEqual([]);
    expect(text()).toContain('Draft');
    expect(text()).toContain('Approve Receipt');
  });

  it('sends no allocations, which is what makes this a Quick Payment', () => {
    const payments = new PaymentsServiceStub();
    page(payments).fillAndSubmit();

    expect(payments.created[0].allocations).toEqual([]);
  });

  it('approves only as a second, separate action, and shows the real code', () => {
    const payments = new PaymentsServiceStub();
    const { text, fillAndSubmit, click } = page(payments);

    fillAndSubmit();
    expect(payments.approvedIds).toEqual([]);

    click('Approve');

    expect(payments.approvedIds).toEqual(['payment-1']);
    // The code comes off the approve response -- numbering happens at Approve, so the create
    // response still says "DRAFT" (phase-17-status.md's bug #3).
    expect(text()).toContain('RCPT-0007 approved');
    expect(text()).not.toContain('DRAFT approved');
  });

  it('freezes the form once a Draft exists, so it cannot drift from the saved row', () => {
    const payments = new PaymentsServiceStub();
    const { fixture, fillAndSubmit } = page(payments);

    fillAndSubmit();

    const form = (fixture.componentInstance as unknown as { form: { disabled: boolean } }).form;
    expect(form.disabled).toBe(true);
  });

  it('tells the user the draft survives when approval fails', () => {
    const payments = new PaymentsServiceStub({ failApprove: true });
    const { text, fillAndSubmit, click } = page(payments);

    fillAndSubmit();
    click('Approve');

    expect(text()).toContain('Transaction Approval queue');
    expect(payments.created.length).toBe(1);
  });
});

class PaymentsServiceStub {
  constructor(private readonly options: { failApprove?: boolean } = {}) {}

  readonly created: PaymentRequest[] = [];
  readonly approvedIds: string[] = [];

  createPayment(_organizationId: string, request: PaymentRequest): Observable<CreatePaymentResult> {
    this.created.push(request);
    return of({ id: 'payment-1', code: 'DRAFT', status: 'Draft' as const });
  }

  approvePayment(_organizationId: string, id: string): Observable<ApprovePaymentResult> {
    if (this.options.failApprove) {
      return throwError(() => new Error('boom'));
    }

    this.approvedIds.push(id);
    return of({ id, code: 'RCPT-0007', status: 'Approved' as const, approvedAt: '2026-09-01T08:00:00Z' });
  }
}
