import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AgeableDocumentType,
  ContactBalanceSummaryDto,
  DocumentAgeDto,
  SalesSummaryMode,
  SalesSummaryReportDto,
  TradeByContactDto,
  TradeByContactMonthlyDto,
  TradeByItemDto,
  TradeByItemMonthlyDto,
  TradeItemGrouping,
} from './trade-reports.models';

/**
 * Phase 26b's thirteen report endpoints. Each mirrored pair is one method taking the route segment,
 * because the backend hardcodes the side at the route -- there is no side parameter to send, and a
 * caller cannot ask one report identity for the other's data.
 *
 * Every `params` object is annotated `Record<string, string>`: a union including `{}` silently
 * resolves `HttpClient.get` to its arraybuffer overload (phase-3 bug #4).
 */
@Injectable({ providedIn: 'root' })
export class TradeReportsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  // ---- Customer Receivable Summary / Supplier Payable Summary ------------------------------

  getContactBalanceSummary(
    organizationId: string,
    route: 'customer-receivable-summary' | 'supplier-payable-summary',
    fromDate: string,
    toDate: string,
    contactGroupId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<ContactBalanceSummaryDto> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get<ContactBalanceSummaryDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  exportContactBalanceSummary(
    organizationId: string,
    route: 'customer-receivable-summary' | 'supplier-payable-summary',
    fromDate: string,
    toDate: string,
    contactGroupId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  // ---- Invoice Age / Purchase Bill Age -----------------------------------------------------

  getDocumentAge(
    organizationId: string,
    route: 'invoice-age' | 'purchase-bill-age',
    fromDate: string,
    asOfDate: string,
    contactId: string | null,
    documentTypes: readonly AgeableDocumentType[],
    page = 1,
    pageSize = 50,
  ): Observable<DocumentAgeDto> {
    return this.http.get<DocumentAgeDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params: this.ageParams(fromDate, asOfDate, contactId, documentTypes, page, pageSize),
    });
  }

  exportDocumentAge(
    organizationId: string,
    route: 'invoice-age' | 'purchase-bill-age',
    fromDate: string,
    asOfDate: string,
    contactId: string | null,
    documentTypes: readonly AgeableDocumentType[],
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params = this.ageParams(fromDate, asOfDate, contactId, documentTypes, page, pageSize);
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  /** `documentType` repeats once per selected type -- the array binding the Minimal API endpoint
   * expects. An empty selection sends nothing, which the handler reads as "all types". */
  private ageParams(
    fromDate: string,
    asOfDate: string,
    contactId: string | null,
    documentTypes: readonly AgeableDocumentType[],
    page: number,
    pageSize: number,
  ): Record<string, string | string[]> {
    const params: Record<string, string | string[]> = {
      fromDate, asOfDate, page: String(page), pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;
    if (documentTypes.length > 0) params['documentType'] = [...documentTypes];
    return params;
  }

  // ---- Sales By Customer / Purchase By Supplier --------------------------------------------

  getTradeByContact(
    organizationId: string,
    route: 'sales-by-customer' | 'purchase-by-supplier',
    fromDate: string,
    toDate: string,
    contactGroupId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<TradeByContactDto> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get<TradeByContactDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  exportTradeByContact(
    organizationId: string,
    route: 'sales-by-customer' | 'purchase-by-supplier',
    fromDate: string,
    toDate: string,
    contactGroupId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  // ---- Sales By Item / Purchase By Item ----------------------------------------------------

  getTradeByItem(
    organizationId: string,
    route: 'sales-by-item' | 'purchase-by-item',
    fromDate: string,
    toDate: string,
    groupBy: TradeItemGrouping,
    productCategoryId: string | null,
    productId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<TradeByItemDto> {
    return this.http.get<TradeByItemDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params: this.itemParams(fromDate, toDate, groupBy, productCategoryId, productId, page, pageSize),
    });
  }

  exportTradeByItem(
    organizationId: string,
    route: 'sales-by-item' | 'purchase-by-item',
    fromDate: string,
    toDate: string,
    groupBy: TradeItemGrouping,
    productCategoryId: string | null,
    productId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params = this.itemParams(fromDate, toDate, groupBy, productCategoryId, productId, page, pageSize);
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  private itemParams(
    fromDate: string,
    toDate: string,
    groupBy: TradeItemGrouping,
    productCategoryId: string | null,
    productId: string | null,
    page: number,
    pageSize: number,
  ): Record<string, string> {
    const params: Record<string, string> = {
      fromDate, toDate, groupBy, page: String(page), pageSize: String(pageSize),
    };
    if (productCategoryId) params['productCategoryId'] = productCategoryId;
    if (productId) params['productId'] = productId;
    return params;
  }

  // ---- The four BS fiscal-year Monthly crosstabs -------------------------------------------

  getTradeByContactMonthly(
    organizationId: string,
    route: 'sales-by-customer-monthly' | 'purchase-by-supplier-monthly',
    fiscalYear: number,
    contactGroupId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<TradeByContactMonthlyDto> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), page: String(page), pageSize: String(pageSize),
    };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get<TradeByContactMonthlyDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  exportTradeByContactMonthly(
    organizationId: string,
    route: 'sales-by-customer-monthly' | 'purchase-by-supplier-monthly',
    fiscalYear: number,
    contactGroupId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getTradeByItemMonthly(
    organizationId: string,
    route: 'sales-by-item-monthly' | 'purchase-by-item-monthly',
    fiscalYear: number,
    page = 1,
    pageSize = 50,
  ): Observable<TradeByItemMonthlyDto> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), page: String(page), pageSize: String(pageSize),
    };
    return this.http.get<TradeByItemMonthlyDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  exportTradeByItemMonthly(
    organizationId: string,
    route: 'sales-by-item-monthly' | 'purchase-by-item-monthly',
    fiscalYear: number,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), full: String(full), page: String(page), pageSize: String(pageSize),
    };
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  // ---- Sales Summary Report ------------------------------------------------------------------

  getSalesSummaryReport(
    organizationId: string,
    fiscalYear: number,
    mode: SalesSummaryMode,
    page = 1,
    pageSize = 50,
  ): Observable<SalesSummaryReportDto> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), mode, page: String(page), pageSize: String(pageSize),
    };
    return this.http.get<SalesSummaryReportDto>(`${this.baseUrl(organizationId)}/reports/sales-summary`, {
      withCredentials: true,
      params,
    });
  }

  exportSalesSummaryReport(
    organizationId: string,
    fiscalYear: number,
    mode: SalesSummaryMode,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fiscalYear: String(fiscalYear), mode, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    return this.http.get(`${this.baseUrl(organizationId)}/reports/sales-summary/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }
}
