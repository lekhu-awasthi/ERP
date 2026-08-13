import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  ApproveInventoryAdjustmentResult,
  ApproveWarehouseTransferResult,
  CreateInventoryAdjustmentResult,
  CreateWarehouseTransferResult,
  InventoryAdjustment,
  InventoryAdjustmentDetail,
  InventoryAdjustmentRequest,
  InventoryAdjustmentStatus,
  InventoryLedgerRowDto,
  StockPositionDto,
  UpdateInventoryAdjustmentResult,
  UpdateWarehouseTransferResult,
  WarehouseTransfer,
  WarehouseTransferDetail,
  WarehouseTransferRequest,
  WarehouseTransferStatus,
} from './inventory.models';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listWarehouseTransfers(organizationId: string, status?: WarehouseTransferStatus): Observable<WarehouseTransfer[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<WarehouseTransfer[]>(`${this.baseUrl(organizationId)}/warehouse-transfers`, {
      withCredentials: true,
      params,
    });
  }

  getWarehouseTransfer(organizationId: string, id: string): Observable<WarehouseTransferDetail> {
    return this.http.get<WarehouseTransferDetail>(`${this.baseUrl(organizationId)}/warehouse-transfers/${id}`, {
      withCredentials: true,
    });
  }

  createWarehouseTransfer(organizationId: string, request: WarehouseTransferRequest): Observable<CreateWarehouseTransferResult> {
    return this.http.post<CreateWarehouseTransferResult>(`${this.baseUrl(organizationId)}/warehouse-transfers`, request, {
      withCredentials: true,
    });
  }

  updateWarehouseTransfer(
    organizationId: string, id: string, request: WarehouseTransferRequest,
  ): Observable<UpdateWarehouseTransferResult> {
    return this.http.put<UpdateWarehouseTransferResult>(`${this.baseUrl(organizationId)}/warehouse-transfers/${id}`, request, {
      withCredentials: true,
    });
  }

  approveWarehouseTransfer(organizationId: string, id: string): Observable<ApproveWarehouseTransferResult> {
    return this.http.post<ApproveWarehouseTransferResult>(
      `${this.baseUrl(organizationId)}/warehouse-transfers/${id}/approve`, null, { withCredentials: true },
    );
  }

  listInventoryAdjustments(organizationId: string, status?: InventoryAdjustmentStatus): Observable<InventoryAdjustment[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<InventoryAdjustment[]>(`${this.baseUrl(organizationId)}/inventory-adjustments`, {
      withCredentials: true,
      params,
    });
  }

  getInventoryAdjustment(organizationId: string, id: string): Observable<InventoryAdjustmentDetail> {
    return this.http.get<InventoryAdjustmentDetail>(`${this.baseUrl(organizationId)}/inventory-adjustments/${id}`, {
      withCredentials: true,
    });
  }

  createInventoryAdjustment(
    organizationId: string, request: InventoryAdjustmentRequest,
  ): Observable<CreateInventoryAdjustmentResult> {
    return this.http.post<CreateInventoryAdjustmentResult>(`${this.baseUrl(organizationId)}/inventory-adjustments`, request, {
      withCredentials: true,
    });
  }

  updateInventoryAdjustment(
    organizationId: string, id: string, request: InventoryAdjustmentRequest,
  ): Observable<UpdateInventoryAdjustmentResult> {
    return this.http.put<UpdateInventoryAdjustmentResult>(`${this.baseUrl(organizationId)}/inventory-adjustments/${id}`, request, {
      withCredentials: true,
    });
  }

  approveInventoryAdjustment(organizationId: string, id: string): Observable<ApproveInventoryAdjustmentResult> {
    return this.http.post<ApproveInventoryAdjustmentResult>(
      `${this.baseUrl(organizationId)}/inventory-adjustments/${id}/approve`, null, { withCredentials: true },
    );
  }

  getStockPosition(organizationId: string, productId?: string, warehouseId?: string): Observable<StockPositionDto[]> {
    const params: Record<string, string> = {};
    if (productId) {
      params['productId'] = productId;
    }
    if (warehouseId) {
      params['warehouseId'] = warehouseId;
    }
    return this.http.get<StockPositionDto[]>(`${this.baseUrl(organizationId)}/inventory/stock-position`, {
      withCredentials: true,
      params,
    });
  }

  getInventoryLedger(organizationId: string, productId: string, warehouseId: string): Observable<InventoryLedgerRowDto[]> {
    return this.http.get<InventoryLedgerRowDto[]>(`${this.baseUrl(organizationId)}/inventory/ledger`, {
      withCredentials: true,
      params: { productId, warehouseId },
    });
  }
}
