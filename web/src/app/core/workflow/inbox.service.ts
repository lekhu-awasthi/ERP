import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import {
  AiDocumentExtractionSetting,
  InboxDocument,
  InboxPrefill,
  InboxTargetType,
  UpdateInboxDocumentRequest,
  UploadedDocumentStatus,
} from './inbox.models';

@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/workflow/inbox-documents`;
  }

  listDocuments(
    organizationId: string,
    options: {
      status?: UploadedDocumentStatus | null;
      linkedTransactionType?: InboxTargetType | null;
      linkedTransactionId?: string | null;
      search?: string | null;
      page?: number;
      pageSize?: number;
    } = {},
  ): Observable<PagedResult<InboxDocument>> {
    const params: Record<string, string> = {
      page: String(options.page ?? 1),
      pageSize: String(options.pageSize ?? 25),
    };
    if (options.status) params['status'] = options.status;
    if (options.linkedTransactionType) params['linkedTransactionType'] = options.linkedTransactionType;
    if (options.linkedTransactionId) params['linkedTransactionId'] = options.linkedTransactionId;
    if (options.search) params['search'] = options.search;

    return this.http.get<PagedResult<InboxDocument>>(this.baseUrl(organizationId), {
      withCredentials: true,
      params,
    });
  }

  getDocument(organizationId: string, id: string): Observable<InboxDocument> {
    return this.http.get<InboxDocument>(`${this.baseUrl(organizationId)}/${id}`, { withCredentials: true });
  }

  /** description/label ride the query string, not the form body -- the endpoint binds a single
   * IFormFile plus route/query parameters, which is what makes ASP.NET Core treat it as a multipart
   * endpoint at all (the same shape as ImportService.createImportJob). */
  uploadDocument(
    organizationId: string,
    file: File,
    description: string | null = null,
    label: string | null = null,
  ): Observable<InboxDocument> {
    const form = new FormData();
    form.append('file', file);

    const params: Record<string, string> = {};
    if (description) params['description'] = description;
    if (label) params['label'] = label;

    return this.http.post<InboxDocument>(this.baseUrl(organizationId), form, { withCredentials: true, params });
  }

  updateDocument(organizationId: string, id: string, request: UpdateInboxDocumentRequest): Observable<InboxDocument> {
    return this.http.put<InboxDocument>(`${this.baseUrl(organizationId)}/${id}`, request, { withCredentials: true });
  }

  deleteDocument(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/${id}`, { withCredentials: true });
  }

  /** The conversion's first half. 403s for a user without the *target type's* own Create key. */
  getPrefill(organizationId: string, id: string, targetType: InboxTargetType): Observable<InboxPrefill> {
    return this.http.get<InboxPrefill>(`${this.baseUrl(organizationId)}/${id}/prefill/${targetType}`, {
      withCredentials: true,
    });
  }

  /** The conversion's second half: the target document already exists, created by its own ordinary
   * Create command with a human having pressed Save. */
  linkDocument(
    organizationId: string,
    id: string,
    transactionType: InboxTargetType,
    transactionId: string,
  ): Observable<InboxDocument> {
    return this.http.post<InboxDocument>(
      `${this.baseUrl(organizationId)}/${id}/link`,
      { transactionType, transactionId },
      { withCredentials: true },
    );
  }

  extract(organizationId: string, id: string): Observable<InboxDocument> {
    return this.http.post<InboxDocument>(`${this.baseUrl(organizationId)}/${id}/extract`, {}, { withCredentials: true });
  }

  clearExtraction(organizationId: string, id: string): Observable<InboxDocument> {
    return this.http.delete<InboxDocument>(`${this.baseUrl(organizationId)}/${id}/extraction`, {
      withCredentials: true,
    });
  }

  /**
   * The authenticated stream URL for rendering a scan inline (an `<img>`/`<iframe>` src).
   *
   * <p>IFileStorage deliberately exposes no public URL, so this is an API route behind the same
   * permission check as everything else -- the browser sends the httpOnly auth cookie with it
   * automatically because the Api's CORS policy allows credentials. Chosen over fetching a Blob and
   * building an object URL because a plain src keeps the preview declarative and needs no manual
   * lifetime management; the cost is that the request cannot carry an explicit header, which it does
   * not need to.</p>
   */
  contentUrl(organizationId: string, id: string): string {
    return `${this.baseUrl(organizationId)}/${id}/content`;
  }

  downloadUrl(organizationId: string, id: string): string {
    return `${this.baseUrl(organizationId)}/${id}/download`;
  }

  downloadDocument(organizationId: string, id: string): Observable<Blob> {
    return this.http.get(this.downloadUrl(organizationId, id), { withCredentials: true, responseType: 'blob' });
  }

  getExtractionSetting(organizationId: string): Observable<AiDocumentExtractionSetting> {
    return this.http.get<AiDocumentExtractionSetting>(
      `${environment.apiBaseUrl}/api/organizations/${organizationId}/ai-document-extraction`,
      { withCredentials: true },
    );
  }

  updateExtractionSetting(organizationId: string, enabled: boolean): Observable<AiDocumentExtractionSetting> {
    return this.http.put<AiDocumentExtractionSetting>(
      `${environment.apiBaseUrl}/api/organizations/${organizationId}/ai-document-extraction`,
      { enabled },
      { withCredentials: true },
    );
  }
}
