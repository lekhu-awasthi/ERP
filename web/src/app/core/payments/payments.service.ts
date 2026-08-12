import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PaymentAllocationInput } from './payments.models';
import {
  ApprovePaymentResult,
  CreatePaymentResult,
  GlLinePreviewDto,
  Payment,
  PaymentDetail,
  PaymentRequest,
  PaymentStatus,
  UpdatePaymentResult,
} from './payments.models';

@Injectable({ providedIn: 'root' })
export class PaymentsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listPayments(organizationId: string, status?: PaymentStatus): Observable<Payment[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<Payment[]>(`${this.baseUrl(organizationId)}/payments`, { withCredentials: true, params });
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

  getDefaultAllocations(organizationId: string, contactId: string, amount: number): Observable<PaymentAllocationInput[]> {
    const params: Record<string, string> = { contactId, amount: amount.toString() };
    return this.http.get<PaymentAllocationInput[]>(`${this.baseUrl(organizationId)}/payments/default-allocations`, {
      withCredentials: true,
      params,
    });
  }

  previewPaymentGlPosting(organizationId: string, accountId: string, amount: number): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/payments/preview-gl-posting`,
      { accountId, amount },
      { withCredentials: true },
    );
  }
}
