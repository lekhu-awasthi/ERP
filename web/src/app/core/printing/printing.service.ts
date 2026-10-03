import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DocumentType } from '../sales/sales.models';

/**
 * Phase 67 -- the two document types whose every copy is counted (the IRD's 2072 computerised-invoicing
 * procedure, §6). The server writes a print row and marks every copy after the first "COPY OF ORIGINAL ·
 * printed N times", so these print with a POST; the GET refuses them (phase-67-status.md Decision C).
 */
export const COUNTED_PRINT_TYPES: readonly DocumentType[] = ['Invoice', 'CreditNote'];

/**
 * Phase 20d's print action -- one generic endpoint for every wired document type (see the
 * backend's PrintDocumentPermissions for which ones). Returns a PDF blob; callers open it in a
 * new tab, letting the browser's native PDF viewer supply Print/Save rather than this app
 * shipping its own print UI (see docs/phase-20d-status.md's rendering-engine decision).
 *
 * The verb is chosen here rather than on each detail page, so no page can print an invoice or a
 * credit note through the uncounted route.
 */
@Injectable({ providedIn: 'root' })
export class PrintingService {
  private readonly http = inject(HttpClient);

  printDocument(organizationId: string, documentType: DocumentType, documentId: string): Observable<Blob> {
    const url = `${environment.apiBaseUrl}/api/organizations/${organizationId}/print/${documentType}/${documentId}`;
    const options = { withCredentials: true, responseType: 'blob' as const };

    const request$ = COUNTED_PRINT_TYPES.includes(documentType)
      ? this.http.post(url, null, options)
      : this.http.get(url, options);

    return request$.pipe(catchError((err: unknown) => from(withParsedBlobError(err)).pipe(switchMap((e) => throwError(() => e)))));
  }
}

/**
 * Phase 67 -- a blob request's error arrives as a Blob, so `extractErrorMessage` found no ProblemDetails
 * in it and every refusal read as "Could not print". The counted print has refusals worth reading (a
 * draft, a void, "printed somewhere else at the same moment, print it again"), so the body is parsed
 * back into the shape every other error has.
 */
async function withParsedBlobError(err: unknown): Promise<unknown> {
  if (!(err instanceof HttpErrorResponse) || !(err.error instanceof Blob)) {
    return err;
  }

  try {
    const body: unknown = JSON.parse(await err.error.text());
    return new HttpErrorResponse({
      error: body,
      headers: err.headers,
      status: err.status,
      statusText: err.statusText,
      url: err.url ?? undefined,
    });
  } catch {
    return err;
  }
}
