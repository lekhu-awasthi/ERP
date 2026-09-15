import { signal } from '@angular/core';

import { FieldError, errorIdFor } from './field-error';

/**
 * Phase 47 — the behaviour the document forms' `aria-invalid` / `aria-describedby` rests on.
 *
 * The interesting assertions are the two about the marking going *away*, because that is where a
 * stored flag would have been wrong and the derived one is right: the page clears and re-sets its
 * `errorMessage` constantly — on every save attempt and for server errors this class knows nothing
 * about — and a field left painted red under a message about something else is worse than no marking
 * at all, since the screen reader would read the wrong field as the one to fix.
 */
describe('FieldError', () => {
  it('marks only the control the current message is about', () => {
    const message = signal<string | null>(null);
    const errors = new FieldError(message);

    errors.fail('invoice-detail-page-customer', 'Select a Customer.');

    expect(message()).toBe('Select a Customer.');
    expect(errors.is('invoice-detail-page-customer')).toBe(true);
    expect(errors.is('invoice-detail-page-warehouse')).toBe(false);
    expect(errors.invalid('invoice-detail-page-customer')).toBe('true');
    expect(errors.describedBy('invoice-detail-page-customer')).toBe('invoice-detail-page-customer-error');
    expect(errors.messageFor('invoice-detail-page-customer')).toBe('Select a Customer.');
  });

  it('still reacts after being read on a form that has not failed yet', () => {
    // The regression the browser pass found, and the one every other test here missed because each
    // of them calls fail() before reading anything. A `computed()` records only the signals it
    // actually reached, so a short-circuit that skips the signal on the first (unfailed) evaluation
    // leaves it with no dependencies and frozen at null forever. On a real form the first read is
    // the initial render, so it was frozen on every document form in the app.
    const message = signal<string | null>(null);
    const errors = new FieldError(message);

    expect(errors.is('invoice-detail-page-customer')).toBe(false);

    errors.fail('invoice-detail-page-customer', 'Select a Customer.');

    expect(errors.is('invoice-detail-page-customer')).toBe(true);
    expect(errors.describedBy('invoice-detail-page-customer')).toBe('invoice-detail-page-customer-error');
  });

  it('never claims a control is valid, only that it is invalid or unknown', () => {
    // `aria-invalid="false"` is a claim that the field has been checked and is fine, which is not
    // what "this form has not been submitted yet" means. Null removes the attribute.
    const errors = new FieldError(signal<string | null>(null));

    expect(errors.invalid('invoice-detail-page-warehouse')).toBeNull();
    expect(errors.describedBy('invoice-detail-page-warehouse')).toBeNull();
  });

  it('drops the marking when the page clears its message', () => {
    const message = signal<string | null>(null);
    const errors = new FieldError(message);

    errors.fail('invoice-detail-page-customer', 'Select a Customer.');
    message.set(null);

    expect(errors.is('invoice-detail-page-customer')).toBe(false);
    expect(errors.describedBy('invoice-detail-page-customer')).toBeNull();
  });

  it('drops the marking when the banner moves on to a message about something else', () => {
    const message = signal<string | null>(null);
    const errors = new FieldError(message);

    errors.fail('invoice-detail-page-customer', 'Select a Customer.');
    message.set('Could not save invoice. Please try again.');

    expect(errors.is('invoice-detail-page-customer')).toBe(false);
  });

  it('moves the marking when a second field fails', () => {
    const message = signal<string | null>(null);
    const errors = new FieldError(message);

    errors.fail('invoice-detail-page-customer', 'Select a Customer.');
    errors.fail('invoice-detail-page-warehouse', 'Select a Warehouse.');

    expect(errors.is('invoice-detail-page-customer')).toBe(false);
    expect(errors.is('invoice-detail-page-warehouse')).toBe(true);
  });

  it('derives the message element id from the control id, in one place', () => {
    // The id the <app-field-error> renders and the id aria-describedby points at come from this
    // function, so the two cannot drift apart.
    expect(errorIdFor('payment-detail-page-amount')).toBe('payment-detail-page-amount-error');
  });

  it('moves focus to the control it failed at', async () => {
    const control = document.createElement('input');
    control.id = 'test-control';
    document.body.append(control);

    try {
      const errors = new FieldError(signal<string | null>(null));
      errors.fail('test-control', 'Enter something.');

      // fail() defers the focus by a microtask so the control is rendered and marked first.
      await Promise.resolve();

      expect(document.activeElement).toBe(control);
    } finally {
      control.remove();
    }
  });
});
