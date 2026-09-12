import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  ExceptionalReportDto,
  InventoryBalanceFilter,
  InventoryLedgerReportDto,
  InventoryMasterReportDto,
  InventoryMovementReportDto,
  InventoryPositionReportDto,
  NetTradingAssetsDto,
  PurchaseReturnRegisterDto,
  SalesReturnRegisterDto,
  UserLogDto,
} from './catalogue-reports.models';

/**
 * Phase 26c's nine reports, in one service for the reason their endpoints share one file: they are
 * one feature -- the read-only, period-filtered, exportable catalogue -- rather than nine features
 * that happen to be reports. Splitting them across SalesService, PurchasingService,
 * InventoryService and AccountingService by which aggregate each reads would put the two return
 * registers in different files, and the whole point of that pair is that they are not mirrors.
 *
 * Every `params` object is annotated `Record<string, string>`: a union including `{}` silently
 * resolves `HttpClient.get` to its arraybuffer overload (phase-3 bug #4).
 */
@Injectable({ providedIn: 'root' })
export class CatalogueReportsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /** Phase 36 -- groupByWarehouse and tagOptionIds are the live drawer's Show Columns checkbox and
   *  its Reporting Tags multi-selects, re-read on Moonbeam 2026-09-11. */
  getInventoryPosition(
    organizationId: string, fromDate: string, toDate: string, categoryId: string | null,
    productId: string | null, warehouseId: string | null, balanceFilter: InventoryBalanceFilter,
    page = 1, pageSize = 50,
    locationId?: string,
    groupByWarehouse = false,
    tagOptionIds: string[] = [],
  ): Observable<InventoryPositionReportDto> {
    const params: Record<string, string | string[]> = {
      ...this.stockParams(fromDate, toDate, categoryId, productId, warehouseId, balanceFilter, page, pageSize, locationId ?? null),
      groupByWarehouse: String(groupByWarehouse),
    };
    if (tagOptionIds.length > 0) params['tagOptionIds'] = tagOptionIds;

    return this.http.get<InventoryPositionReportDto>(
      `${this.baseUrl(organizationId)}/reports/inventory-position`,
      { withCredentials: true, params },
    );
  }

  exportInventoryPosition(
    organizationId: string, fromDate: string, toDate: string, categoryId: string | null,
    productId: string | null, warehouseId: string | null, balanceFilter: InventoryBalanceFilter,
    full: boolean, page: number, pageSize: number, locationId?: string,
    groupByWarehouse = false, tagOptionIds: string[] = [],
  ): Observable<Blob> {
    const params: Record<string, string | string[]> = {
      ...this.stockParams(fromDate, toDate, categoryId, productId, warehouseId, balanceFilter, page, pageSize, locationId ?? null),
      groupByWarehouse: String(groupByWarehouse),
    };
    if (tagOptionIds.length > 0) params['tagOptionIds'] = tagOptionIds;
    if (locationId) params['locationId'] = locationId;
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/inventory-position/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getInventoryMovement(
    organizationId: string, fromDate: string, toDate: string, categoryId: string | null,
    productId: string | null, warehouseId: string | null, page = 1, pageSize = 50,
    locationId?: string,
  ): Observable<InventoryMovementReportDto> {
    return this.http.get<InventoryMovementReportDto>(
      `${this.baseUrl(organizationId)}/reports/inventory-movement`,
      { withCredentials: true, params: this.stockParams(fromDate, toDate, categoryId, productId, warehouseId, null, page, pageSize, locationId ?? null) },
    );
  }

  exportInventoryMovement(
    organizationId: string, fromDate: string, toDate: string, categoryId: string | null,
    productId: string | null, warehouseId: string | null, full: boolean, page: number, pageSize: number, locationId?: string,
  ): Observable<Blob> {
    const params = this.stockParams(fromDate, toDate, categoryId, productId, warehouseId, null, page, pageSize, locationId ?? null);
    if (locationId) params['locationId'] = locationId;
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/inventory-movement/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getInventoryLedger(
    organizationId: string, fromDate: string, toDate: string, productId: string,
    warehouseId: string | null, page = 1, pageSize = 50, locationId?: string,
  ): Observable<InventoryLedgerReportDto> {
    const params: Record<string, string> = {
      fromDate, toDate, productId, page: String(page), pageSize: String(pageSize),
    };
    if (locationId) params['locationId'] = locationId;
    if (warehouseId) params['warehouseId'] = warehouseId;
    return this.http.get<InventoryLedgerReportDto>(
      `${this.baseUrl(organizationId)}/reports/inventory-ledger`, { withCredentials: true, params });
  }

  exportInventoryLedger(
    organizationId: string, fromDate: string, toDate: string, productId: string,
    warehouseId: string | null, full: boolean, page: number, pageSize: number, locationId?: string,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, productId, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (locationId) params['locationId'] = locationId;
    if (warehouseId) params['warehouseId'] = warehouseId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/inventory-ledger/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getInventoryMaster(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    productId: string | null, documentType: string | null, page = 1, pageSize = 50,
    locationId?: string,
  ): Observable<InventoryMasterReportDto> {
    return this.http.get<InventoryMasterReportDto>(
      `${this.baseUrl(organizationId)}/reports/inventory-master`,
      { withCredentials: true, params: this.masterParams(fromDate, toDate, contactId, productId, documentType, page, pageSize, locationId ?? null) },
    );
  }

  exportInventoryMaster(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    productId: string | null, documentType: string | null, full: boolean, page: number, pageSize: number, locationId?: string,
  ): Observable<Blob> {
    const params = this.masterParams(fromDate, toDate, contactId, productId, documentType, page, pageSize, locationId ?? null);
    if (locationId) params['locationId'] = locationId;
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/inventory-master/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getSalesReturnRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    page = 1, pageSize = 50,
    locationId?: string,
  ): Observable<SalesReturnRegisterDto> {
    return this.http.get<SalesReturnRegisterDto>(
      `${this.baseUrl(organizationId)}/reports/sales-return-register`,
      { withCredentials: true, params: this.registerParams(fromDate, toDate, contactId, page, pageSize, locationId ?? null) },
    );
  }

  exportSalesReturnRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    full: boolean, page: number, pageSize: number, locationId?: string,
  ): Observable<Blob> {
    const params = this.registerParams(fromDate, toDate, contactId, page, pageSize, locationId ?? null);
    if (locationId) params['locationId'] = locationId;
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/sales-return-register/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getPurchaseReturnRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    page = 1, pageSize = 50,
    locationId?: string,
  ): Observable<PurchaseReturnRegisterDto> {
    return this.http.get<PurchaseReturnRegisterDto>(
      `${this.baseUrl(organizationId)}/reports/purchase-return-register`,
      { withCredentials: true, params: this.registerParams(fromDate, toDate, contactId, page, pageSize, locationId ?? null) },
    );
  }

  exportPurchaseReturnRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    full: boolean, page: number, pageSize: number, locationId?: string,
  ): Observable<Blob> {
    const params = this.registerParams(fromDate, toDate, contactId, page, pageSize, locationId ?? null);
    if (locationId) params['locationId'] = locationId;
    params['full'] = String(full);
    return this.http.get(`${this.baseUrl(organizationId)}/reports/purchase-return-register/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  getNetTradingAssets(
    organizationId: string, fromDate: string, toDate: string, compare: boolean, excludeAdvance: boolean,
    locationId?: string,
  ): Observable<NetTradingAssetsDto> {
    return this.http.get<NetTradingAssetsDto>(
      `${this.baseUrl(organizationId)}/reports/net-trading-assets`,
      { withCredentials: true, params: this.withLocation({ fromDate, toDate, compare: String(compare), excludeAdvance: String(excludeAdvance) }, locationId) },
    );
  }

  exportNetTradingAssets(
    organizationId: string, fromDate: string, toDate: string, compare: boolean, excludeAdvance: boolean,
    locationId?: string,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/net-trading-assets/export`, {
      withCredentials: true,
      params: this.withLocation({ fromDate, toDate, compare: String(compare), excludeAdvance: String(excludeAdvance) }, locationId),
      responseType: 'blob',
    });
  }

  getExceptionalReport(
    organizationId: string, fromDate: string, toDate: string,
  ): Observable<ExceptionalReportDto> {
    return this.http.get<ExceptionalReportDto>(
      `${this.baseUrl(organizationId)}/reports/exceptional-report`,
      { withCredentials: true, params: { fromDate, toDate } },
    );
  }

  exportExceptionalReport(organizationId: string, fromDate: string, toDate: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/exceptional-report/export`, {
      withCredentials: true, params: { fromDate, toDate }, responseType: 'blob',
    });
  }

  getUserLog(
    organizationId: string, fromDate: string, toDate: string, userId: string | null,
    page = 1, pageSize = 50,
  ): Observable<UserLogDto> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (userId) params['userId'] = userId;
    return this.http.get<UserLogDto>(`${this.baseUrl(organizationId)}/reports/user-log`, {
      withCredentials: true, params,
    });
  }

  exportUserLog(
    organizationId: string, fromDate: string, toDate: string, userId: string | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (userId) params['userId'] = userId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/user-log/export`, {
      withCredentials: true, params, responseType: 'blob',
    });
  }

  /**
   * Phase 35b -- the Billing Location filter for a report that builds its params inline rather than
   * through one of the three builders below. A function, so `Record<string, string>` stays the
   * declared type: a union that includes `{}` silently resolves HttpClient.get to its arraybuffer
   * overload (phase-3 bug #4).
   */
  private withLocation(params: Record<string, string>, locationId?: string): Record<string, string> {
    return locationId ? { ...params, locationId } : params;
  }

  private stockParams(
    fromDate: string, toDate: string, categoryId: string | null, productId: string | null,
    warehouseId: string | null, balanceFilter: InventoryBalanceFilter | null, page: number, pageSize: number,
    locationId: string | null,
  ): Record<string, string> {
    const params: Record<string, string> = {
      fromDate, toDate, page: String(page), pageSize: String(pageSize),
    };
    if (categoryId) params['categoryId'] = categoryId;
    if (productId) params['productId'] = productId;
    if (warehouseId) params['warehouseId'] = warehouseId;
    if (balanceFilter && balanceFilter !== 'All') params['balanceFilter'] = balanceFilter;
    if (locationId) params['locationId'] = locationId;
    return params;
  }

  private masterParams(
    fromDate: string, toDate: string, contactId: string | null, productId: string | null,
    documentType: string | null, page: number, pageSize: number,
    locationId: string | null,
  ): Record<string, string> {
    const params: Record<string, string> = {
      fromDate, toDate, page: String(page), pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;
    if (productId) params['productId'] = productId;
    if (documentType) params['documentType'] = documentType;
    if (locationId) params['locationId'] = locationId;
    return params;
  }

  private registerParams(
    fromDate: string, toDate: string, contactId: string | null, page: number, pageSize: number,
    locationId: string | null,
  ): Record<string, string> {
    const params: Record<string, string> = {
      fromDate, toDate, page: String(page), pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;
    if (locationId) params['locationId'] = locationId;
    return params;
  }
}
