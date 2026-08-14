import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  ApproveDebitNoteResult,
  ApproveExpenseResult,
  ApprovePurchaseBillResult,
  ApprovePurchaseOrderResult,
  CreateDebitNoteResult,
  CreateExpenseResult,
  CreatePurchaseBillResult,
  CreatePurchaseOrderResult,
  DebitNote,
  DebitNoteConversionTemplate,
  DebitNoteDetail,
  DebitNoteRequest,
  DebitNoteStatus,
  Expense,
  ExpenseDetail,
  ExpenseLineInput,
  ExpenseRequest,
  ExpenseStatus,
  GlLinePreviewDto,
  PurchaseBill,
  PurchaseBillConversionTemplate,
  PurchaseBillDetail,
  PurchaseBillLineInput,
  PurchaseBillRequest,
  PurchaseBillStatus,
  PurchaseMasterReportDto,
  PurchaseOrder,
  PurchaseOrderDetail,
  PurchaseOrderRequest,
  PurchaseOrderStatus,
  UpdateDebitNoteResult,
  UpdateExpenseResult,
  UpdatePurchaseBillResult,
  UpdatePurchaseOrderResult,
} from './purchasing.models';

@Injectable({ providedIn: 'root' })
export class PurchasingService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listPurchaseOrders(organizationId: string, status?: PurchaseOrderStatus): Observable<PurchaseOrder[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<PurchaseOrder[]>(`${this.baseUrl(organizationId)}/purchase-orders`, { withCredentials: true, params });
  }

  getPurchaseOrder(organizationId: string, id: string): Observable<PurchaseOrderDetail> {
    return this.http.get<PurchaseOrderDetail>(`${this.baseUrl(organizationId)}/purchase-orders/${id}`, { withCredentials: true });
  }

  createPurchaseOrder(organizationId: string, request: PurchaseOrderRequest): Observable<CreatePurchaseOrderResult> {
    return this.http.post<CreatePurchaseOrderResult>(`${this.baseUrl(organizationId)}/purchase-orders`, request, {
      withCredentials: true,
    });
  }

  updatePurchaseOrder(organizationId: string, id: string, request: PurchaseOrderRequest): Observable<UpdatePurchaseOrderResult> {
    return this.http.put<UpdatePurchaseOrderResult>(`${this.baseUrl(organizationId)}/purchase-orders/${id}`, request, {
      withCredentials: true,
    });
  }

  approvePurchaseOrder(organizationId: string, id: string): Observable<ApprovePurchaseOrderResult> {
    return this.http.post<ApprovePurchaseOrderResult>(`${this.baseUrl(organizationId)}/purchase-orders/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  getPurchaseBillConversionTemplate(organizationId: string, purchaseOrderId: string): Observable<PurchaseBillConversionTemplate> {
    return this.http.get<PurchaseBillConversionTemplate>(
      `${this.baseUrl(organizationId)}/purchase-orders/${purchaseOrderId}/purchase-bill-conversion-template`,
      { withCredentials: true },
    );
  }

  listPurchaseBills(organizationId: string, status?: PurchaseBillStatus): Observable<PurchaseBill[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<PurchaseBill[]>(`${this.baseUrl(organizationId)}/purchase-bills`, { withCredentials: true, params });
  }

  getPurchaseBill(organizationId: string, id: string): Observable<PurchaseBillDetail> {
    return this.http.get<PurchaseBillDetail>(`${this.baseUrl(organizationId)}/purchase-bills/${id}`, { withCredentials: true });
  }

  createPurchaseBill(organizationId: string, request: PurchaseBillRequest): Observable<CreatePurchaseBillResult> {
    return this.http.post<CreatePurchaseBillResult>(`${this.baseUrl(organizationId)}/purchase-bills`, request, {
      withCredentials: true,
    });
  }

  updatePurchaseBill(organizationId: string, id: string, request: PurchaseBillRequest): Observable<UpdatePurchaseBillResult> {
    return this.http.put<UpdatePurchaseBillResult>(`${this.baseUrl(organizationId)}/purchase-bills/${id}`, request, {
      withCredentials: true,
    });
  }

  approvePurchaseBill(organizationId: string, id: string): Observable<ApprovePurchaseBillResult> {
    return this.http.post<ApprovePurchaseBillResult>(`${this.baseUrl(organizationId)}/purchase-bills/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  previewPurchaseBillGlPosting(
    organizationId: string,
    lines: PurchaseBillLineInput[],
    tdsTypeId: string | null,
  ): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/purchase-bills/preview-gl-posting`,
      { lines, tdsTypeId },
      { withCredentials: true },
    );
  }

  getDebitNoteConversionTemplate(organizationId: string, purchaseBillId: string): Observable<DebitNoteConversionTemplate> {
    return this.http.get<DebitNoteConversionTemplate>(
      `${this.baseUrl(organizationId)}/purchase-bills/${purchaseBillId}/debit-note-conversion-template`,
      { withCredentials: true },
    );
  }

  listExpenses(organizationId: string, status?: ExpenseStatus): Observable<Expense[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<Expense[]>(`${this.baseUrl(organizationId)}/expenses`, { withCredentials: true, params });
  }

  getExpense(organizationId: string, id: string): Observable<ExpenseDetail> {
    return this.http.get<ExpenseDetail>(`${this.baseUrl(organizationId)}/expenses/${id}`, { withCredentials: true });
  }

  createExpense(organizationId: string, request: ExpenseRequest): Observable<CreateExpenseResult> {
    return this.http.post<CreateExpenseResult>(`${this.baseUrl(organizationId)}/expenses`, request, { withCredentials: true });
  }

  updateExpense(organizationId: string, id: string, request: ExpenseRequest): Observable<UpdateExpenseResult> {
    return this.http.put<UpdateExpenseResult>(`${this.baseUrl(organizationId)}/expenses/${id}`, request, {
      withCredentials: true,
    });
  }

  approveExpense(organizationId: string, id: string): Observable<ApproveExpenseResult> {
    return this.http.post<ApproveExpenseResult>(`${this.baseUrl(organizationId)}/expenses/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  previewExpenseGlPosting(
    organizationId: string,
    lines: ExpenseLineInput[],
    tdsApplicable: boolean,
    tdsTypeId: string | null,
  ): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/expenses/preview-gl-posting`,
      { lines, tdsApplicable, tdsTypeId },
      { withCredentials: true },
    );
  }

  listDebitNotes(organizationId: string, status?: DebitNoteStatus): Observable<DebitNote[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<DebitNote[]>(`${this.baseUrl(organizationId)}/debit-notes`, { withCredentials: true, params });
  }

  getDebitNote(organizationId: string, id: string): Observable<DebitNoteDetail> {
    return this.http.get<DebitNoteDetail>(`${this.baseUrl(organizationId)}/debit-notes/${id}`, { withCredentials: true });
  }

  createDebitNote(organizationId: string, request: DebitNoteRequest): Observable<CreateDebitNoteResult> {
    return this.http.post<CreateDebitNoteResult>(`${this.baseUrl(organizationId)}/debit-notes`, request, {
      withCredentials: true,
    });
  }

  updateDebitNote(organizationId: string, id: string, request: DebitNoteRequest): Observable<UpdateDebitNoteResult> {
    return this.http.put<UpdateDebitNoteResult>(`${this.baseUrl(organizationId)}/debit-notes/${id}`, request, {
      withCredentials: true,
    });
  }

  approveDebitNote(organizationId: string, id: string): Observable<ApproveDebitNoteResult> {
    return this.http.post<ApproveDebitNoteResult>(`${this.baseUrl(organizationId)}/debit-notes/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  getPurchaseMasterReport(
    organizationId: string,
    fromDate: string,
    toDate: string,
    contactId: string | null,
    productId: string | null,
    warehouseId: string | null,
  ): Observable<PurchaseMasterReportDto> {
    const params: Record<string, string> = { fromDate, toDate };
    if (contactId) params['contactId'] = contactId;
    if (productId) params['productId'] = productId;
    if (warehouseId) params['warehouseId'] = warehouseId;

    return this.http.get<PurchaseMasterReportDto>(`${this.baseUrl(organizationId)}/reports/purchase-master-report`, {
      withCredentials: true,
      params,
    });
  }
}
