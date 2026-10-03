import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import { PosSessionStatus, PosTab } from './pos.models';
import { PosOrderStatus } from './pos-restaurant.models';
import {
  PosDashboard,
  PosDayReport,
  PosOrderReport,
  PosPaymentSummary,
  PosPaymentType,
  PosSessionRow,
} from './pos-reports.models';

/** A period, and optionally one location: what every POS report is asked for. */
export interface PosReportPeriod {
  fromDate: string;
  toDate: string;
  locationId?: string | null;
}

/**
 * Phase 66 -- the POS reports and the dashboard. The ones on a POS key are under `/pos`, the Payment
 * Summary (a `Reports.` key) under `/reports`, as on the server.
 */
@Injectable({ providedIn: 'root' })
export class PosReportsService {
  private readonly http = inject(HttpClient);

  getDashboard(organizationId: string, period: PosReportPeriod): Observable<PosDashboard> {
    return this.http.get<PosDashboard>(`${this.baseUrl(organizationId)}/pos/dashboard`, {
      withCredentials: true,
      params: this.periodParams(period),
    });
  }

  getDayReport(organizationId: string, period: PosReportPeriod): Observable<PosDayReport> {
    return this.http.get<PosDayReport>(`${this.baseUrl(organizationId)}/pos/reports/day`, {
      withCredentials: true,
      params: this.periodParams(period),
    });
  }

  exportDayReport(organizationId: string, period: PosReportPeriod): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/pos/reports/day/export`, {
      withCredentials: true,
      params: this.periodParams(period),
      responseType: 'blob',
    });
  }

  listSessions(
    organizationId: string, period: PosReportPeriod, status: PosSessionStatus | null, search: string,
    page: number, pageSize: number,
  ): Observable<PagedResult<PosSessionRow>> {
    return this.http.get<PagedResult<PosSessionRow>>(`${this.baseUrl(organizationId)}/pos/sessions`, {
      withCredentials: true,
      params: this.sessionParams(period, status, search, { page: String(page), pageSize: String(pageSize) }),
    });
  }

  exportSessions(
    organizationId: string, period: PosReportPeriod, status: PosSessionStatus | null, search: string,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/pos/sessions/export`, {
      withCredentials: true,
      params: this.sessionParams(period, status, search, {}),
      responseType: 'blob',
    });
  }

  getOrderReport(
    organizationId: string, period: PosReportPeriod, status: PosOrderStatus | null, orderType: PosTab | null,
    page: number, pageSize: number,
  ): Observable<PosOrderReport> {
    return this.http.get<PosOrderReport>(`${this.baseUrl(organizationId)}/pos/reports/orders`, {
      withCredentials: true,
      params: this.orderParams(period, status, orderType, { page: String(page), pageSize: String(pageSize) }),
    });
  }

  exportOrderReport(
    organizationId: string, period: PosReportPeriod, status: PosOrderStatus | null, orderType: PosTab | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/pos/reports/orders/export`, {
      withCredentials: true,
      params: this.orderParams(
        period, status, orderType, { full: String(full), page: String(page), pageSize: String(pageSize) }),
      responseType: 'blob',
    });
  }

  getPaymentSummary(
    organizationId: string, period: PosReportPeriod, type: PosPaymentType | null, paymentModeId: string | null,
    page: number, pageSize: number,
  ): Observable<PosPaymentSummary> {
    return this.http.get<PosPaymentSummary>(`${this.baseUrl(organizationId)}/reports/pos-payment-summary`, {
      withCredentials: true,
      params: this.paymentParams(period, type, paymentModeId, { page: String(page), pageSize: String(pageSize) }),
    });
  }

  exportPaymentSummary(
    organizationId: string, period: PosReportPeriod, type: PosPaymentType | null, paymentModeId: string | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/pos-payment-summary/export`, {
      withCredentials: true,
      params: this.paymentParams(
        period, type, paymentModeId, { full: String(full), page: String(page), pageSize: String(pageSize) }),
      responseType: 'blob',
    });
  }

  private periodParams(period: PosReportPeriod): Record<string, string> {
    const params: Record<string, string> = { fromDate: period.fromDate, toDate: period.toDate };
    if (period.locationId) params['locationId'] = period.locationId;
    return params;
  }

  private sessionParams(
    period: PosReportPeriod, status: PosSessionStatus | null, search: string, extra: Record<string, string>,
  ): Record<string, string> {
    const params = { ...this.periodParams(period), ...extra };
    if (status) params['status'] = status;
    if (search.trim()) params['search'] = search.trim();
    return params;
  }

  private orderParams(
    period: PosReportPeriod, status: PosOrderStatus | null, orderType: PosTab | null, extra: Record<string, string>,
  ): Record<string, string> {
    const params = { ...this.periodParams(period), ...extra };
    if (status) params['status'] = status;
    if (orderType) params['orderType'] = orderType;
    return params;
  }

  private paymentParams(
    period: PosReportPeriod, type: PosPaymentType | null, paymentModeId: string | null, extra: Record<string, string>,
  ): Record<string, string> {
    const params = { ...this.periodParams(period), ...extra };
    if (type) params['type'] = type;
    if (paymentModeId) params['paymentModeId'] = paymentModeId;
    return params;
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }
}
