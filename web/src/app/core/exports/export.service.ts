import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import { ExportJobSummary } from './export.models';

/**
 * Roadmap Phase 21b -- full-tenant data export (FR-2.8 / NFR-4.3). Sits beside `ImportService` for
 * the same reason that one sits outside `ConfigurationService`: these routes are at
 * `/api/organizations/{id}/export-jobs`, and one of them is a file transfer.
 */
@Injectable({ providedIn: 'root' })
export class ExportService {
  private readonly http = inject(HttpClient);

  /** No body: an export takes no parameters beyond the tenant (Decision A). */
  createExportJob(organizationId: string): Observable<ExportJobSummary> {
    return this.http.post<ExportJobSummary>(
      `${this.baseUrl(organizationId)}/export-jobs`,
      {},
      { withCredentials: true },
    );
  }

  listExportJobs(
    organizationId: string,
    page = 1,
    pageSize = 25,
  ): Observable<PagedResult<ExportJobSummary>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    return this.http.get<PagedResult<ExportJobSummary>>(`${this.baseUrl(organizationId)}/export-jobs`, {
      withCredentials: true,
      params,
    });
  }

  cancelExportJob(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl(organizationId)}/export-jobs/${id}/cancel`,
      {},
      { withCredentials: true },
    );
  }

  /**
   * Fetched as a Blob through HttpClient rather than linked with an `<a href>`, so the request
   * carries the auth cookie the same way every other API call does and a 403 surfaces as an error
   * the page can show instead of a broken download. That matters more here than anywhere else in
   * the app: this response is the tenant's entire data set, and the endpoint behind it is the only
   * door it can leave through (Decision F).
   */
  downloadExport(organizationId: string, id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/export-jobs/${id}/download`, {
      withCredentials: true,
      responseType: 'blob',
    });
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }
}
