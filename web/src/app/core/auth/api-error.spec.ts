import { HttpErrorResponse } from '@angular/common/http';

import { extractErrorMessage, extractWarningKind } from './api-error';

/**
 * Phase 31 -- `warningKind` is the contract that lets an approval screen know *which* confirmable
 * warning a 422 carried, now that three of them exist and one approval can trip more than one. If
 * this discriminator is wrong the failure is silent and dangerous: confirming a stock shortfall
 * would waive a credit-limit breach the user never saw. Hence the negative cases as well as the
 * positive one.
 */
describe('extractWarningKind', () => {
  function problem(status: number, body: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error: body });
  }

  it('reads the kind off a 422', () => {
    expect(extractWarningKind(problem(422, { title: 'x', warningKind: 'CreditLimit' }))).toBe('CreditLimit');
    expect(extractWarningKind(problem(422, { title: 'x', warningKind: 'StockAvailability' }))).toBe(
      'StockAvailability',
    );
    expect(extractWarningKind(problem(422, { title: 'x', warningKind: 'NegativeCashBalance' }))).toBe(
      'NegativeCashBalance',
    );
  });

  it('is null for any other status, even one carrying the field', () => {
    expect(extractWarningKind(problem(409, { title: 'x', warningKind: 'CreditLimit' }))).toBeNull();
    expect(extractWarningKind(problem(403, { title: 'x' }))).toBeNull();
  });

  it('is null when a 422 carries no kind, so an unknown warning is never mistaken for a known one', () => {
    expect(extractWarningKind(problem(422, { title: 'x' }))).toBeNull();
    expect(extractWarningKind('not an http error')).toBeNull();
  });

  it('leaves the message unwrapping alone', () => {
    expect(extractErrorMessage(problem(422, { title: 'Past their credit limit.', warningKind: 'CreditLimit' }))).toBe(
      'Past their credit limit.',
    );
  });
});
