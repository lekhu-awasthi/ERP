import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { extractErrorMessage } from '../auth/api-error';
import { COUNTED_PRINT_TYPES, PrintingService } from './printing.service';

/**
 * Phase 67 -- an invoice and a credit note print as a counted copy (POST, which writes the print row);
 * every other document keeps the GET. The server refuses the GET for the two counted types, so a page
 * that reached it would fail rather than print an unmarked copy -- this pins that no page can.
 */
describe('PrintingService', () => {
  let service: PrintingService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/api/organizations/org-1/print`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PrintingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('counts exactly the invoice and the credit note', () => {
    expect([...COUNTED_PRINT_TYPES]).toEqual(['Invoice', 'CreditNote']);
  });

  for (const type of ['Invoice', 'CreditNote'] as const) {
    it(`prints ${type} with a POST that the server counts`, () => {
      service.printDocument('org-1', type, 'doc-1').subscribe();

      const request = httpMock.expectOne(`${base}/${type}/doc-1`);
      expect(request.request.method).toBe('POST');
      expect(request.request.withCredentials).toBe(true);
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['%PDF']));
    });
  }

  it('reads a refusal out of the blob body, so the page can show the server message', async () => {
    const failure = new Promise<unknown>((resolve) =>
      service.printDocument('org-1', 'Invoice', 'doc-1').subscribe({ error: resolve }),
    );

    const body = { title: 'Invoice INV0001 was printed somewhere else at the same moment. Print it again.' };
    httpMock
      .expectOne(`${base}/Invoice/doc-1`)
      .flush(new Blob([JSON.stringify(body)], { type: 'application/problem+json' }), { status: 409, statusText: 'Conflict' });

    expect(extractErrorMessage(await failure)).toBe(body.title);
  });

  it('prints every other document with the uncounted GET', () => {
    service.printDocument('org-1', 'Quotation', 'doc-2').subscribe();

    const request = httpMock.expectOne(`${base}/Quotation/doc-2`);
    expect(request.request.method).toBe('GET');
    request.flush(new Blob(['%PDF']));
  });
});
