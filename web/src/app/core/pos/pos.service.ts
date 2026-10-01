import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../common/paged-result';

import { environment } from '../../../environments/environment';
import { PosMode } from '../organizations/organizations.models';
import {
  ClosePosSessionRequest,
  CreatePosRefundRequest,
  CreatePosRefundResult,
  CreatePosSaleRequest,
  CreatePosSaleResult,
  OpenPosSessionRequest,
  PosConfiguration,
  PosLocationSettings,
  PosProduct,
  PosReceipt,
  PosRefundableSale,
  PosRefundPreview,
  PosRefundReceipt,
  PosSaleMatch,
  PosSession,
  PosSessionRefund,
  PosSessionSale,
  PosTill,
  PosTillSummary,
  PreviewPosRefundRequest,
  RecordPosCashMovementRequest,
  UpdatePosLocationSettingsRequest,
} from './pos.models';

/** Phase 60 -- Configurations > Point of Sale; phase 62 -- the till's own calls, under the same `/pos` prefix. */
@Injectable({ providedIn: 'root' })
export class PosService {
  private readonly http = inject(HttpClient);

  getConfiguration(organizationId: string): Observable<PosConfiguration> {
    return this.http.get<PosConfiguration>(`${this.baseUrl(organizationId)}/configuration`, { withCredentials: true });
  }

  getLocationSettings(organizationId: string, locationId: string): Observable<PosLocationSettings> {
    return this.http.get<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/settings`, {
      withCredentials: true,
    });
  }

  updateLocationSettings(
    organizationId: string,
    locationId: string,
    request: UpdatePosLocationSettingsRequest,
  ): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/settings`, request, {
      withCredentials: true,
    });
  }

  setLocationMode(organizationId: string, locationId: string, posMode: PosMode): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/mode`, { posMode }, {
      withCredentials: true,
    });
  }

  setLocationPaymentModes(
    organizationId: string,
    locationId: string,
    paymentModeIds: string[],
  ): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(
      `${this.locationUrl(organizationId, locationId)}/payment-modes`,
      { paymentModeIds },
      { withCredentials: true },
    );
  }

  // ---- Phase 62: the till -----------------------------------------------------------------------

  /** The launcher: tills the caller may open a drawer at, with their own open session there. */
  listTills(organizationId: string): Observable<PosTillSummary[]> {
    return this.http.get<PosTillSummary[]>(`${this.baseUrl(organizationId)}/tills`, { withCredentials: true });
  }

  getTill(organizationId: string, locationId: string): Observable<PosTill> {
    return this.http.get<PosTill>(`${this.baseUrl(organizationId)}/tills/${locationId}`, { withCredentials: true });
  }

  /** The grid. `code` is a scanner's exact match on barcode, code or SKU; `search` is typed. */
  listProducts(
    organizationId: string,
    locationId: string,
    query: { search?: string; code?: string; categoryId?: string; page?: number; pageSize?: number },
  ): Observable<PagedResult<PosProduct>> {
    const params: Record<string, string> = {};
    if (query.search) params['search'] = query.search;
    if (query.code) params['code'] = query.code;
    if (query.categoryId) params['categoryId'] = query.categoryId;
    if (query.page) params['page'] = String(query.page);
    if (query.pageSize) params['pageSize'] = String(query.pageSize);
    return this.http.get<PagedResult<PosProduct>>(`${this.baseUrl(organizationId)}/tills/${locationId}/products`, {
      withCredentials: true,
      params,
    });
  }

  /** The caller's open session at one till, or null (the endpoint answers 204). */
  getMySession(organizationId: string, locationId: string): Observable<PosSession | null> {
    return this.http.get<PosSession | null>(`${this.locationUrl(organizationId, locationId)}/sessions/mine`, {
      withCredentials: true,
    });
  }

  openSession(organizationId: string, request: OpenPosSessionRequest): Observable<PosSession> {
    return this.http.post<PosSession>(`${this.baseUrl(organizationId)}/sessions`, request, { withCredentials: true });
  }

  getSession(organizationId: string, sessionId: string): Observable<PosSession> {
    return this.http.get<PosSession>(`${this.baseUrl(organizationId)}/sessions/${sessionId}`, { withCredentials: true });
  }

  listSessionSales(organizationId: string, sessionId: string): Observable<PosSessionSale[]> {
    return this.http.get<PosSessionSale[]>(`${this.baseUrl(organizationId)}/sessions/${sessionId}/sales`, {
      withCredentials: true,
    });
  }

  recordCashMovement(
    organizationId: string,
    sessionId: string,
    request: RecordPosCashMovementRequest,
  ): Observable<PosSession> {
    return this.http.post<PosSession>(`${this.baseUrl(organizationId)}/sessions/${sessionId}/cash-movements`, request, {
      withCredentials: true,
    });
  }

  closeSession(organizationId: string, sessionId: string, request: ClosePosSessionRequest): Observable<PosSession> {
    return this.http.post<PosSession>(`${this.baseUrl(organizationId)}/sessions/${sessionId}/close`, request, {
      withCredentials: true,
    });
  }

  createSale(organizationId: string, request: CreatePosSaleRequest): Observable<CreatePosSaleResult> {
    return this.http.post<CreatePosSaleResult>(`${this.baseUrl(organizationId)}/sales`, request, {
      withCredentials: true,
    });
  }

  /** Every call is a printing the server counts: 1 is the original, every later one a marked copy. */
  printReceipt(organizationId: string, invoiceId: string): Observable<PosReceipt> {
    return this.http.post<PosReceipt>(`${this.baseUrl(organizationId)}/sales/${invoiceId}/prints`, {}, {
      withCredentials: true,
    });
  }

  // ---- Phase 63: refunds -----------------------------------------------------------------------

  /** Approved till sales of one location whose number contains `search`, newest first. */
  findSales(organizationId: string, locationId: string, search: string): Observable<PosSaleMatch[]> {
    const params: Record<string, string> = {};
    if (search) params['search'] = search;
    return this.http.get<PosSaleMatch[]>(`${this.baseUrl(organizationId)}/tills/${locationId}/sales`, {
      withCredentials: true,
      params,
    });
  }

  getRefundableSale(organizationId: string, invoiceId: string): Observable<PosRefundableSale> {
    return this.http.get<PosRefundableSale>(`${this.baseUrl(organizationId)}/sales/${invoiceId}/refundable`, {
      withCredentials: true,
    });
  }

  /** The refund's figure before it is made, from the refund's own planner on the server. */
  previewRefund(organizationId: string, request: PreviewPosRefundRequest): Observable<PosRefundPreview> {
    return this.http.post<PosRefundPreview>(`${this.baseUrl(organizationId)}/refunds/preview`, request, {
      withCredentials: true,
    });
  }

  createRefund(organizationId: string, request: CreatePosRefundRequest): Observable<CreatePosRefundResult> {
    return this.http.post<CreatePosRefundResult>(`${this.baseUrl(organizationId)}/refunds`, request, {
      withCredentials: true,
    });
  }

  listSessionRefunds(organizationId: string, sessionId: string): Observable<PosSessionRefund[]> {
    return this.http.get<PosSessionRefund[]>(`${this.baseUrl(organizationId)}/sessions/${sessionId}/refunds`, {
      withCredentials: true,
    });
  }

  /** Every call is a printing the server counts, as for a sale's receipt. */
  printRefundReceipt(organizationId: string, creditNoteId: string): Observable<PosRefundReceipt> {
    return this.http.post<PosRefundReceipt>(`${this.baseUrl(organizationId)}/refunds/${creditNoteId}/prints`, {}, {
      withCredentials: true,
    });
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/pos`;
  }

  private locationUrl(organizationId: string, locationId: string): string {
    return `${this.baseUrl(organizationId)}/locations/${locationId}`;
  }
}
