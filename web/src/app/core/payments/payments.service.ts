import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult } from '../common/paged-result';

import { PaymentAllocationInput, PaymentDirection } from './payments.models';
import {
  AllocatablePaymentDto,
  ApplyPaymentAllocationResult,
  ApprovePaymentResult,
  ChequeDashboardSummaryDto,
  ChequeDto,
  ChequeStatus,
  CreatePaymentResult,
  GlLinePreviewDto,
  Payment,
  PaymentDetail,
  PaymentRequest,
  PaymentStatus,
  TransitionChequeStatusResult,
  UpdatePaymentResult,
  VoidPaymentResult,
} from './payments.models';
import { DocumentType } from '../sales/sales.models';

@Injectable({ providedIn: 'root' })
export class PaymentsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listPayments(
    organizationId: string,
    status?: PaymentStatus,
    direction?: PaymentDirection,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<Payment>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    if (direction) params['direction'] = direction;
    return this.http.get<PagedResult<Payment>>(`${this.baseUrl(organizationId)}/payments`, { withCredentials: true, params });
  }

  getPayment(organizationId: string, id: string): Observable<PaymentDetail> {
    return this.http.get<PaymentDetail>(`${this.baseUrl(organizationId)}/payments/${id}`, { withCredentials: true });
  }

  createPayment(organizationId: string, request: PaymentRequest): Observable<CreatePaymentResult> {
    return this.http.post<CreatePaymentResult>(`${this.baseUrl(organizationId)}/payments`, request, { withCredentials: true });
  }

  updatePayment(organizationId: string, id: string, request: PaymentRequest): Observable<UpdatePaymentResult> {
    return this.http.put<UpdatePaymentResult>(`${this.baseUrl(organizationId)}/payments/${id}`, request, {
      withCredentials: true,
    });
  }

  approvePayment(organizationId: string, id: string): Observable<ApprovePaymentResult> {
    return this.http.post<ApprovePaymentResult>(`${this.baseUrl(organizationId)}/payments/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  voidPayment(organizationId: string, id: string): Observable<VoidPaymentResult> {
    return this.http.post<VoidPaymentResult>(`${this.baseUrl(organizationId)}/payments/${id}/void`, null, {
      withCredentials: true,
    });
  }

  getDefaultAllocations(
    organizationId: string,
    contactId: string,
    amount: number,
    direction: PaymentDirection,
  ): Observable<PaymentAllocationInput[]> {
    const params: Record<string, string> = { contactId, amount: amount.toString(), direction };
    return this.http.get<PaymentAllocationInput[]>(`${this.baseUrl(organizationId)}/payments/default-allocations`, {
      withCredentials: true,
      params,
    });
  }

  listCheques(
    organizationId: string,
    direction?: PaymentDirection,
    status?: ChequeStatus,
    contactId?: string,
    fromDate?: string,
    toDate?: string,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<ChequeDto>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (direction) params['direction'] = direction;
    if (status) params['status'] = status;
    if (contactId) params['contactId'] = contactId;
    if (fromDate) params['fromDate'] = fromDate;
    if (toDate) params['toDate'] = toDate;
    return this.http.get<PagedResult<ChequeDto>>(`${this.baseUrl(organizationId)}/cheques`, { withCredentials: true, params });
  }

  chequeDashboardSummary(
    organizationId: string,
    fromDate?: string,
    toDate?: string,
    contactId?: string,
  ): Observable<ChequeDashboardSummaryDto> {
    const params: Record<string, string> = {};
    if (fromDate) params['fromDate'] = fromDate;
    if (toDate) params['toDate'] = toDate;
    if (contactId) params['contactId'] = contactId;
    return this.http.get<ChequeDashboardSummaryDto>(`${this.baseUrl(organizationId)}/cheques/dashboard-summary`, {
      withCredentials: true,
      params,
    });
  }

  transitionChequeStatus(organizationId: string, id: string, newStatus: ChequeStatus): Observable<TransitionChequeStatusResult> {
    return this.http.post<TransitionChequeStatusResult>(
      `${this.baseUrl(organizationId)}/cheques/${id}/transition`,
      { newStatus },
      { withCredentials: true },
    );
  }

  listAllocatablePayments(
    organizationId: string,
    direction: PaymentDirection,
    showAllocated = false,
    contactId?: string,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<AllocatablePaymentDto>> {
    const params: Record<string, string> = {
      direction,
      showAllocated: String(showAllocated),
      page: String(page),
      pageSize: String(pageSize),
    };
    if (contactId) params['contactId'] = contactId;
    return this.http.get<PagedResult<AllocatablePaymentDto>>(`${this.baseUrl(organizationId)}/payments/allocatable`, {
      withCredentials: true,
      params,
    });
  }

  applyPaymentAllocation(
    organizationId: string,
    sourceType: DocumentType,
    sourceId: string,
    parentDocumentId: string | null,
    targetDocumentType: DocumentType,
    targetDocumentId: string,
    amount: number,
  ): Observable<ApplyPaymentAllocationResult> {
    return this.http.post<ApplyPaymentAllocationResult>(
      `${this.baseUrl(organizationId)}/payment-allocations/apply`,
      { sourceType, sourceId, parentDocumentId, targetDocumentType, targetDocumentId, amount },
      { withCredentials: true },
    );
  }

  previewPaymentGlPosting(
    organizationId: string,
    accountId: string,
    amount: number,
    direction: PaymentDirection,
  ): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/payments/preview-gl-posting`,
      { accountId, amount, direction },
      { withCredentials: true },
    );
  }
}
