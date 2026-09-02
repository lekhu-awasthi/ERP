import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';
import {
  ApproveProductionJournalResult,
  BillOfMaterialsDetail,
  BillOfMaterialsListItem,
  BillOfMaterialsRequest,
  BomTemplate,
  CreateDocumentResult,
  ProductionJournalConversionTemplate,
  ProductionJournalDetail,
  ProductionJournalListItem,
  ProductionJournalRequest,
  ProductionJournalStatus,
  ProductionOrderDetail,
  ProductionOrderListItem,
  ProductionOrderRequest,
  ProductionOrderStatus,
  ProductionPlanningReport,
  ProductionSummaryReport,
  ProductionVarianceRow,
} from './manufacturing.models';

@Injectable({ providedIn: 'root' })
export class ManufacturingService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  // ---- Bill of Materials ----

  listBillsOfMaterials(
    organizationId: string,
    search?: string,
    isActive?: boolean,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<BillOfMaterialsListItem>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (search) params['search'] = search;
    if (isActive !== undefined) params['isActive'] = String(isActive);
    return this.http.get<PagedResult<BillOfMaterialsListItem>>(`${this.baseUrl(organizationId)}/bills-of-materials`, {
      withCredentials: true,
      params,
    });
  }

  getBillOfMaterials(organizationId: string, id: string): Observable<BillOfMaterialsDetail> {
    return this.http.get<BillOfMaterialsDetail>(`${this.baseUrl(organizationId)}/bills-of-materials/${id}`, {
      withCredentials: true,
    });
  }

  createBillOfMaterials(organizationId: string, request: BillOfMaterialsRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.baseUrl(organizationId)}/bills-of-materials`, request, {
      withCredentials: true,
    });
  }

  updateBillOfMaterials(
    organizationId: string,
    id: string,
    request: BillOfMaterialsRequest,
  ): Observable<{ id: string }> {
    return this.http.put<{ id: string }>(`${this.baseUrl(organizationId)}/bills-of-materials/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteBillOfMaterials(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/bills-of-materials/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * "LOAD BOM". Returns null (a 204) when the product has no recipe, which is an ordinary answer
   * rather than an error -- the caller simply leaves the lines it already has alone.
   */
  getBomTemplate(organizationId: string, productId: string, outputQuantity: number): Observable<BomTemplate | null> {
    return this.http.get<BomTemplate | null>(`${this.baseUrl(organizationId)}/bom-template`, {
      withCredentials: true,
      params: { productId, outputQuantity: String(outputQuantity) },
    });
  }

  // ---- Production Order ----

  listProductionOrders(
    organizationId: string,
    status?: ProductionOrderStatus,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<ProductionOrderListItem>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<ProductionOrderListItem>>(`${this.baseUrl(organizationId)}/production-orders`, {
      withCredentials: true,
      params,
    });
  }

  getProductionOrder(organizationId: string, id: string): Observable<ProductionOrderDetail> {
    return this.http.get<ProductionOrderDetail>(`${this.baseUrl(organizationId)}/production-orders/${id}`, {
      withCredentials: true,
    });
  }

  createProductionOrder(organizationId: string, request: ProductionOrderRequest): Observable<CreateDocumentResult> {
    return this.http.post<CreateDocumentResult>(`${this.baseUrl(organizationId)}/production-orders`, request, {
      withCredentials: true,
    });
  }

  updateProductionOrder(
    organizationId: string,
    id: string,
    request: ProductionOrderRequest,
  ): Observable<CreateDocumentResult> {
    return this.http.put<CreateDocumentResult>(`${this.baseUrl(organizationId)}/production-orders/${id}`, request, {
      withCredentials: true,
    });
  }

  approveProductionOrder(organizationId: string, id: string): Observable<CreateDocumentResult> {
    return this.http.post<CreateDocumentResult>(
      `${this.baseUrl(organizationId)}/production-orders/${id}/approve`,
      {},
      { withCredentials: true },
    );
  }

  voidProductionOrder(organizationId: string, id: string): Observable<CreateDocumentResult> {
    return this.http.post<CreateDocumentResult>(
      `${this.baseUrl(organizationId)}/production-orders/${id}/void`,
      {},
      { withCredentials: true },
    );
  }

  getProductionJournalTemplate(
    organizationId: string,
    productionOrderId: string,
  ): Observable<ProductionJournalConversionTemplate> {
    return this.http.get<ProductionJournalConversionTemplate>(
      `${this.baseUrl(organizationId)}/production-orders/${productionOrderId}/production-journal-template`,
      { withCredentials: true },
    );
  }

  // ---- Production Journal ----

  listProductionJournals(
    organizationId: string,
    status?: ProductionJournalStatus,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<ProductionJournalListItem>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<ProductionJournalListItem>>(
      `${this.baseUrl(organizationId)}/production-journals`,
      { withCredentials: true, params },
    );
  }

  getProductionJournal(organizationId: string, id: string): Observable<ProductionJournalDetail> {
    return this.http.get<ProductionJournalDetail>(`${this.baseUrl(organizationId)}/production-journals/${id}`, {
      withCredentials: true,
    });
  }

  createProductionJournal(organizationId: string, request: ProductionJournalRequest): Observable<CreateDocumentResult> {
    return this.http.post<CreateDocumentResult>(`${this.baseUrl(organizationId)}/production-journals`, request, {
      withCredentials: true,
    });
  }

  updateProductionJournal(
    organizationId: string,
    id: string,
    request: ProductionJournalRequest,
  ): Observable<CreateDocumentResult> {
    return this.http.put<CreateDocumentResult>(`${this.baseUrl(organizationId)}/production-journals/${id}`, request, {
      withCredentials: true,
    });
  }

  approveProductionJournal(
    organizationId: string,
    id: string,
    overrideWarning = false,
  ): Observable<ApproveProductionJournalResult> {
    const params: Record<string, string> = {};
    if (overrideWarning) params['overrideWarning'] = 'true';
    return this.http.post<ApproveProductionJournalResult>(
      `${this.baseUrl(organizationId)}/production-journals/${id}/approve`,
      {},
      { withCredentials: true, params },
    );
  }

  voidProductionJournal(organizationId: string, id: string): Observable<CreateDocumentResult> {
    return this.http.post<CreateDocumentResult>(
      `${this.baseUrl(organizationId)}/production-journals/${id}/void`,
      {},
      { withCredentials: true },
    );
  }

  // ---- Reports ----

  productionSummary(
    organizationId: string,
    fromDate: string,
    toDate: string,
    productId?: string,
    page = 1,
    pageSize = 50,
  ): Observable<ProductionSummaryReport> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (productId) params['productId'] = productId;
    return this.http.get<ProductionSummaryReport>(`${this.baseUrl(organizationId)}/reports/production-summary`, {
      withCredentials: true,
      params,
    });
  }

  productionVariance(
    organizationId: string,
    fromDate: string,
    toDate: string,
    productId?: string,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<ProductionVarianceRow>> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (productId) params['productId'] = productId;
    return this.http.get<PagedResult<ProductionVarianceRow>>(
      `${this.baseUrl(organizationId)}/reports/production-variance`,
      { withCredentials: true, params },
    );
  }

  productionPlanning(
    organizationId: string,
    productId: string,
    quantity: number,
    warehouseId?: string,
  ): Observable<ProductionPlanningReport> {
    const params: Record<string, string> = { productId, quantity: String(quantity) };
    if (warehouseId) params['warehouseId'] = warehouseId;
    return this.http.get<ProductionPlanningReport>(`${this.baseUrl(organizationId)}/reports/production-planning`, {
      withCredentials: true,
      params,
    });
  }
}
