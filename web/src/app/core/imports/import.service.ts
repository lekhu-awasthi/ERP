import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import {
  ImportEntityType,
  ImportJobDetail,
  ImportJobSummary,
  ImportMode,
} from './import.models';

/**
 * Roadmap Phase 21a -- bulk import (FR-2.9 / NFR-4.3).
 *
 * Not folded into ConfigurationService despite living under Configurations in the UI: these routes
 * sit at `/api/organizations/{id}/import-jobs`, outside that service's `/configuration` base, and
 * two of the four are file transfers rather than JSON.
 */
@Injectable({ providedIn: 'root' })
export class ImportService {
  private readonly http = inject(HttpClient);

  createImportJob(
    organizationId: string,
    entityType: ImportEntityType,
    mode: ImportMode,
    file: File,
  ): Observable<ImportJobSummary> {
    const form = new FormData();
    form.append('file', file);

    // entityType and mode ride the query string, not the form body: the endpoint binds a single
    // IFormFile plus route/query parameters, which is what makes ASP.NET Core treat it as a
    // multipart endpoint at all.
    return this.http.post<ImportJobSummary>(`${this.baseUrl(organizationId)}/import-jobs`, form, {
      withCredentials: true,
      params: { entityType, mode },
    });
  }

  /**
   * @param entityTypes Restricts the history to these upload types; omit for all of them. The
   * Import / Export screen and Phase 21c's Migration screen share this endpoint and the job table,
   * and this filter is what keeps each showing only its own uploads.
   */
  listImportJobs(
    organizationId: string,
    entityTypes: readonly ImportEntityType[] | null = null,
    page = 1,
    pageSize = 25,
  ): Observable<PagedResult<ImportJobSummary>> {
    const params: Record<string, string | string[]> = { page: String(page), pageSize: String(pageSize) };
    if (entityTypes && entityTypes.length > 0) params['entityTypes'] = [...entityTypes];

    return this.http.get<PagedResult<ImportJobSummary>>(`${this.baseUrl(organizationId)}/import-jobs`, {
      withCredentials: true,
      params,
    });
  }

  /** The progress poll: job status/counts plus a page of row outcomes in one round trip. Defaults to
   * failed rows only, because on a 5,000-row import those are the rows anybody looks at. */
  getImportJob(
    organizationId: string,
    id: string,
    failedRowsOnly = true,
    page = 1,
    pageSize = 50,
  ): Observable<ImportJobDetail> {
    const params: Record<string, string> = {
      failedRowsOnly: String(failedRowsOnly),
      page: String(page),
      pageSize: String(pageSize),
    };
    return this.http.get<ImportJobDetail>(`${this.baseUrl(organizationId)}/import-jobs/${id}`, {
      withCredentials: true,
      params,
    });
  }

  cancelImportJob(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl(organizationId)}/import-jobs/${id}/cancel`,
      {},
      { withCredentials: true },
    );
  }

  /** Fetched as a Blob through HttpClient rather than linked with an <a href>, so the request
   * carries the auth cookie the same way every other API call does and a 403 surfaces as an error
   * the page can show instead of a broken download. */
  downloadTemplate(organizationId: string, entityType: ImportEntityType): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/import-templates/${entityType}`, {
      withCredentials: true,
      responseType: 'blob',
    });
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }
}
