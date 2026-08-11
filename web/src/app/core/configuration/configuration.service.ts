import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  CreateCreditTermRequest,
  CreatePaymentModeRequest,
  CreditTerm,
  PaymentMode,
  UpdateCreditTermRequest,
  UpdatePaymentModeRequest,
} from './configuration.models';

@Injectable({ providedIn: 'root' })
export class ConfigurationService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/configuration`;
  }

  listCreditTerms(organizationId: string): Observable<CreditTerm[]> {
    return this.http.get<CreditTerm[]>(`${this.baseUrl(organizationId)}/credit-terms`, { withCredentials: true });
  }

  createCreditTerm(organizationId: string, request: CreateCreditTermRequest): Observable<CreditTerm> {
    return this.http.post<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms`, request, {
      withCredentials: true,
    });
  }

  updateCreditTerm(organizationId: string, id: string, request: UpdateCreditTermRequest): Observable<CreditTerm> {
    return this.http.put<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteCreditTerm(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/credit-terms/${id}`, { withCredentials: true });
  }

  listPaymentModes(organizationId: string): Observable<PaymentMode[]> {
    return this.http.get<PaymentMode[]>(`${this.baseUrl(organizationId)}/payment-modes`, { withCredentials: true });
  }

  createPaymentMode(organizationId: string, request: CreatePaymentModeRequest): Observable<PaymentMode> {
    return this.http.post<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes`, request, {
      withCredentials: true,
    });
  }

  updatePaymentMode(organizationId: string, id: string, request: UpdatePaymentModeRequest): Observable<PaymentMode> {
    return this.http.put<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes/${id}`, request, {
      withCredentials: true,
    });
  }

  deletePaymentMode(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/payment-modes/${id}`, { withCredentials: true });
  }
}
