import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import { DocumentType, TransactionReportingTagDto } from '../sales/sales.models';
import {
  Bank,
  CreateBankRequest,
  CreateCreditTermRequest,
  CreateDealStageRequest,
  CreateLeadSourceRequest,
  CreatePaymentModeRequest,
  CreateTaskTypeRequest,
  CreateTdsTypeRequest,
  CreditTerm,
  CreateReportingTagCategoryRequest,
  CreateReportingTagOptionRequest,
  DealStage,
  LeadSource,
  PaymentMode,
  ReportingTagCategory,
  ReportingTagOption,
  TaskType,
  TdsType,
  UpdateBankRequest,
  UpdateCreditTermRequest,
  UpdateDealStageRequest,
  UpdateLeadSourceRequest,
  UpdatePaymentModeRequest,
  UpdateReportingTagCategoryRequest,
  UpdateReportingTagOptionRequest,
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

  listBanks(organizationId: string): Observable<Bank[]> {
    return this.listAll<Bank>(`${this.baseUrl(organizationId)}/banks`);
  }

  createBank(organizationId: string, request: CreateBankRequest): Observable<Bank> {
    return this.http.post<Bank>(`${this.baseUrl(organizationId)}/banks`, request, { withCredentials: true });
  }

  updateBank(organizationId: string, id: string, request: UpdateBankRequest): Observable<Bank> {
    return this.http.put<Bank>(`${this.baseUrl(organizationId)}/banks/${id}`, request, { withCredentials: true });
  }

  deleteBank(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/banks/${id}`, { withCredentials: true });
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

  // Reporting Tags (Phase 2 backend) -- Phase 19 is the first frontend consumer: the picker on
  // Quotation/Invoice detail pages and every affected report's filter drawer, plus the admin
  // management screen (Configurations > Reporting Tags) below.
  listReportingTagCategories(organizationId: string): Observable<ReportingTagCategory[]> {
    return this.listAll<ReportingTagCategory>(`${this.baseUrl(organizationId)}/reporting-tag-categories`);
  }

  createReportingTagCategory(
    organizationId: string,
    request: CreateReportingTagCategoryRequest,
  ): Observable<ReportingTagCategory> {
    return this.http.post<ReportingTagCategory>(`${this.baseUrl(organizationId)}/reporting-tag-categories`, request, {
      withCredentials: true,
    });
  }

  updateReportingTagCategory(
    organizationId: string,
    id: string,
    request: UpdateReportingTagCategoryRequest,
  ): Observable<ReportingTagCategory> {
    return this.http.put<ReportingTagCategory>(
      `${this.baseUrl(organizationId)}/reporting-tag-categories/${id}`,
      request,
      { withCredentials: true },
    );
  }

  deleteReportingTagCategory(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/reporting-tag-categories/${id}`, {
      withCredentials: true,
    });
  }

  listReportingTagOptions(organizationId: string): Observable<ReportingTagOption[]> {
    return this.listAll<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options`);
  }

  createReportingTagOption(
    organizationId: string,
    request: CreateReportingTagOptionRequest,
  ): Observable<ReportingTagOption> {
    return this.http.post<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options`, request, {
      withCredentials: true,
    });
  }

  updateReportingTagOption(
    organizationId: string,
    id: string,
    request: UpdateReportingTagOptionRequest,
  ): Observable<ReportingTagOption> {
    return this.http.put<ReportingTagOption>(`${this.baseUrl(organizationId)}/reporting-tag-options/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteReportingTagOption(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/reporting-tag-options/${id}`, {
      withCredentials: true,
    });
  }

  getTransactionReportingTags(
    organizationId: string, documentType: DocumentType, documentId: string,
  ): Observable<TransactionReportingTagDto[]> {
    return this.http.get<TransactionReportingTagDto[]>(
      `${this.baseUrl(organizationId)}/reporting-tags/${documentType}/${documentId}`,
      { withCredentials: true },
    );
  }

  setTransactionReportingTags(
    organizationId: string, documentType: DocumentType, documentId: string, tagOptionIds: string[],
  ): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl(organizationId)}/reporting-tags/${documentType}/${documentId}`,
      { tagOptionIds },
      { withCredentials: true },
    );
  }
}
