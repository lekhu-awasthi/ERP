import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
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

  /** Lookup screens (Phase 16c) are bounded master data -- no visible pager, just request
   * everything in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, { withCredentials: true, params: { page: '1', pageSize: String(MAX_PAGE_SIZE) } })
      .pipe(map((result) => result.items));
  }

  listCreditTerms(organizationId: string): Observable<CreditTerm[]> {
    return this.listAll<CreditTerm>(`${this.baseUrl(organizationId)}/credit-terms`);
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
    return this.listAll<PaymentMode>(`${this.baseUrl(organizationId)}/payment-modes`);
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
    return this.listAll<TdsType>(`${this.baseUrl(organizationId)}/tds-types`);
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
    return this.listAll<TaskType>(`${this.baseUrl(organizationId)}/task-types`);
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
    return this.listAll<LeadSource>(`${this.baseUrl(organizationId)}/lead-sources`);
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
    return this.listAll<DealStage>(`${this.baseUrl(organizationId)}/deal-stages`);
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
