import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  CreateCreditTermRequest,
  CreateDealStageRequest,
  CreateLeadSourceRequest,
  CreatePaymentModeRequest,
  CreateTaskTypeRequest,
  CreateTdsTypeRequest,
  CreditTerm,
  DealStage,
  LeadSource,
  PaymentMode,
  TaskType,
  TdsType,
  UpdateCreditTermRequest,
  UpdateDealStageRequest,
  UpdateLeadSourceRequest,
  UpdatePaymentModeRequest,
  UpdateTaskTypeRequest,
  UpdateTdsTypeRequest,
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

  listTdsTypes(organizationId: string): Observable<TdsType[]> {
    return this.http.get<TdsType[]>(`${this.baseUrl(organizationId)}/tds-types`, { withCredentials: true });
  }

  createTdsType(organizationId: string, request: CreateTdsTypeRequest): Observable<TdsType> {
    return this.http.post<TdsType>(`${this.baseUrl(organizationId)}/tds-types`, request, { withCredentials: true });
  }

  updateTdsType(organizationId: string, id: string, request: UpdateTdsTypeRequest): Observable<TdsType> {
    return this.http.put<TdsType>(`${this.baseUrl(organizationId)}/tds-types/${id}`, request, { withCredentials: true });
  }

  deleteTdsType(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/tds-types/${id}`, { withCredentials: true });
  }

  listTaskTypes(organizationId: string): Observable<TaskType[]> {
    return this.http.get<TaskType[]>(`${this.baseUrl(organizationId)}/task-types`, { withCredentials: true });
  }

  createTaskType(organizationId: string, request: CreateTaskTypeRequest): Observable<TaskType> {
    return this.http.post<TaskType>(`${this.baseUrl(organizationId)}/task-types`, request, { withCredentials: true });
  }

  updateTaskType(organizationId: string, id: string, request: UpdateTaskTypeRequest): Observable<TaskType> {
    return this.http.put<TaskType>(`${this.baseUrl(organizationId)}/task-types/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteTaskType(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/task-types/${id}`, { withCredentials: true });
  }

  listLeadSources(organizationId: string): Observable<LeadSource[]> {
    return this.http.get<LeadSource[]>(`${this.baseUrl(organizationId)}/lead-sources`, { withCredentials: true });
  }

  createLeadSource(organizationId: string, request: CreateLeadSourceRequest): Observable<LeadSource> {
    return this.http.post<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources`, request, { withCredentials: true });
  }

  updateLeadSource(organizationId: string, id: string, request: UpdateLeadSourceRequest): Observable<LeadSource> {
    return this.http.put<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteLeadSource(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/lead-sources/${id}`, { withCredentials: true });
  }

  listDealStages(organizationId: string): Observable<DealStage[]> {
    return this.http.get<DealStage[]>(`${this.baseUrl(organizationId)}/deal-stages`, { withCredentials: true });
  }

  createDealStage(organizationId: string, request: CreateDealStageRequest): Observable<DealStage> {
    return this.http.post<DealStage>(`${this.baseUrl(organizationId)}/deal-stages`, request, { withCredentials: true });
  }

  updateDealStage(organizationId: string, id: string, request: UpdateDealStageRequest): Observable<DealStage> {
    return this.http.put<DealStage>(`${this.baseUrl(organizationId)}/deal-stages/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteDealStage(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/deal-stages/${id}`, { withCredentials: true });
  }
}
