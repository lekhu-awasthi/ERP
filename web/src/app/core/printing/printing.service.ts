import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DocumentType } from '../sales/sales.models';

/**
 * Phase 20d's print action -- one generic endpoint for every wired document type (see the
 * backend's PrintDocumentPermissions for which ones). Returns a PDF blob; callers open it in a
 * new tab, letting the browser's native PDF viewer supply Print/Save rather than this app
 * shipping its own print UI (see docs/phase-20d-status.md's rendering-engine decision).
 */
@Injectable({ providedIn: 'root' })
export class PrintingService {
  private readonly http = inject(HttpClient);

  printDocument(organizationId: string, documentType: DocumentType, documentId: string): Observable<Blob> {
    return this.http.get(`${environment.apiBaseUrl}/api/organizations/${organizationId}/print/${documentType}/${documentId}`, {
      withCredentials: true,
      responseType: 'blob',
    });
  }
}
