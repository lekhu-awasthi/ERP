import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import {
  AnnexThirteenReportDto,
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
  PurchaseRegisterDto,
  PurchaseOrder,
  PurchaseOrderDetail,
  PurchaseOrderRequest,
  PurchaseOrderStatus,
  TdsReportDto,
  UpdateDebitNoteResult,
  UpdateExpenseResult,
  UpdatePurchaseBillResult,
  UpdatePurchaseOrderResult,
  VoidDebitNoteResult,
  VoidExpenseResult,
  VoidPurchaseBillResult,
  VoidPurchaseOrderResult,
} from './purchasing.models';

@Injectable({ providedIn: 'root' })
export class PurchasingService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listPurchaseOrders(
    organizationId: string, status?: PurchaseOrderStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<PurchaseOrder>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<PurchaseOrder>>(`${this.baseUrl(organizationId)}/purchase-orders`, {
      withCredentials: true,
      params,
    });
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

  voidPurchaseOrder(organizationId: string, id: string): Observable<VoidPurchaseOrderResult> {
    return this.http.post<VoidPurchaseOrderResult>(`${this.baseUrl(organizationId)}/purchase-orders/${id}/void`, null, {
      withCredentials: true,
    });
  }

  getPurchaseBillConversionTemplate(organizationId: string, purchaseOrderId: string): Observable<PurchaseBillConversionTemplate> {
    return this.http.get<PurchaseBillConversionTemplate>(
      `${this.baseUrl(organizationId)}/purchase-orders/${purchaseOrderId}/purchase-bill-conversion-template`,
      { withCredentials: true },
    );
  }

  listPurchaseBills(
    organizationId: string, status?: PurchaseBillStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<PurchaseBill>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<PurchaseBill>>(`${this.baseUrl(organizationId)}/purchase-bills`, {
      withCredentials: true,
      params,
    });
  }

  /** Picker use (e.g. a Supplier Payment's allocation target list) -- everything in one page, no pager. */
  listAllPurchaseBills(organizationId: string, status?: PurchaseBillStatus): Observable<PurchaseBill[]> {
    return this.listPurchaseBills(organizationId, status, 1, MAX_PAGE_SIZE).pipe(map((result) => result.items));
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

  voidPurchaseBill(organizationId: string, id: string): Observable<VoidPurchaseBillResult> {
    return this.http.post<VoidPurchaseBillResult>(`${this.baseUrl(organizationId)}/purchase-bills/${id}/void`, null, {
      withCredentials: true,
    });
  }

  previewPurchaseBillGlPosting(
    organizationId: string,
    lines: PurchaseBillLineInput[],
    tdsTypeId: string | null,
    discountPct: number,
  ): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/purchase-bills/preview-gl-posting`,
      { lines, tdsTypeId, discountPct },
      { withCredentials: true },
    );
  }

  getDebitNoteConversionTemplate(organizationId: string, purchaseBillId: string): Observable<DebitNoteConversionTemplate> {
    return this.http.get<DebitNoteConversionTemplate>(
      `${this.baseUrl(organizationId)}/purchase-bills/${purchaseBillId}/debit-note-conversion-template`,
      { withCredentials: true },
    );
  }

  listExpenses(organizationId: string, status?: ExpenseStatus, page = 1, pageSize = 50): Observable<PagedResult<Expense>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<Expense>>(`${this.baseUrl(organizationId)}/expenses`, { withCredentials: true, params });
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

  voidExpense(organizationId: string, id: string): Observable<VoidExpenseResult> {
    return this.http.post<VoidExpenseResult>(`${this.baseUrl(organizationId)}/expenses/${id}/void`, null, {
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

  listDebitNotes(organizationId: string, status?: DebitNoteStatus, page = 1, pageSize = 50): Observable<PagedResult<DebitNote>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<DebitNote>>(`${this.baseUrl(organizationId)}/debit-notes`, { withCredentials: true, params });
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

  voidDebitNote(organizationId: string, id: string): Observable<VoidDebitNoteResult> {
    return this.http.post<VoidDebitNoteResult>(`${this.baseUrl(organizationId)}/debit-notes/${id}/void`, null, {
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
    page = 1,
    pageSize = 50,
  ): Observable<PurchaseMasterReportDto> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (contactId) params['contactId'] = contactId;
    if (productId) params['productId'] = productId;
    if (warehouseId) params['warehouseId'] = warehouseId;

    return this.http.get<PurchaseMasterReportDto>(`${this.baseUrl(organizationId)}/reports/purchase-master-report`, {
      withCredentials: true,
      params,
    });
  }

  exportPurchaseMasterReport(
    organizationId: string,
    fromDate: string,
    toDate: string,
    contactId: string | null,
    productId: string | null,
    warehouseId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;
    if (productId) params['productId'] = productId;
    if (warehouseId) params['warehouseId'] = warehouseId;

    return this.http.get(`${this.baseUrl(organizationId)}/reports/purchase-master-report/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getTdsReport(
    organizationId: string, fromDate: string, toDate: string, page = 1, pageSize = 50,
  ): Observable<TdsReportDto> {
    return this.http.get<TdsReportDto>(`${this.baseUrl(organizationId)}/reports/tds-report`, {
      withCredentials: true,
      params: { fromDate, toDate, page: String(page), pageSize: String(pageSize) },
    });
  }

  exportTdsReport(
    organizationId: string, fromDate: string, toDate: string, full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/tds-report/export`, {
      withCredentials: true,
      params: { fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize) },
      responseType: 'blob',
    });
  }

  getAnnexThirteenReport(
    organizationId: string,
    fromDate: string,
    toDate: string,
    thresholdAmount: number,
    page = 1,
    pageSize = 50,
  ): Observable<AnnexThirteenReportDto> {
    const params: Record<string, string> = {
      fromDate, toDate, thresholdAmount: thresholdAmount.toString(), page: String(page), pageSize: String(pageSize),
    };
    return this.http.get<AnnexThirteenReportDto>(`${this.baseUrl(organizationId)}/reports/annex-thirteen`, {
      withCredentials: true,
      params,
    });
  }

  exportAnnexThirteenReport(
    organizationId: string,
    fromDate: string,
    toDate: string,
    thresholdAmount: number,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/annex-thirteen/export`, {
      withCredentials: true,
      params: {
        fromDate, toDate, thresholdAmount: thresholdAmount.toString(), full: String(full),
        page: String(page), pageSize: String(pageSize),
      },
      responseType: 'blob',
    });
  }

  getPurchaseRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null, page = 1, pageSize = 50,
  ): Observable<PurchaseRegisterDto> {
    const params: Record<string, string> = { fromDate, toDate, page: String(page), pageSize: String(pageSize) };
    if (contactId) params['contactId'] = contactId;

    return this.http.get<PurchaseRegisterDto>(`${this.baseUrl(organizationId)}/reports/purchase-register`, {
      withCredentials: true,
      params,
    });
  }

  exportPurchaseRegister(
    organizationId: string, fromDate: string, toDate: string, contactId: string | null,
    full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;

    return this.http.get(`${this.baseUrl(organizationId)}/reports/purchase-register/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }
}
