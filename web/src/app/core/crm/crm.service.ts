import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AdjustSmsCreditRequest,
  CreateDealRequest,
  CreateDealResult,
  DealListDto,
  DealStatus,
  MoveDealToStageRequest,
  SendSmsRequest,
  SendSmsResult,
  SmsCreditAdjustmentResult,
  SmsCreditLedgerDto,
  SmsLogListDto,
  SmsTemplateListDto,
  SmsTemplateRequest,
  SmsTemplateResult,
  UpdateDealRequest,
} from './crm.models';

@Injectable({ providedIn: 'root' })
export class CrmService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listDeals(
    organizationId: string,
    contactId: string | null,
    status: DealStatus | null,
    page = 1,
    pageSize = 50,
  ): Observable<DealListDto> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (contactId) {
      params['contactId'] = contactId;
    }
    if (status) {
      params['status'] = status;
    }
    return this.http.get<DealListDto>(`${this.baseUrl(organizationId)}/deals`, { withCredentials: true, params });
  }

  createDeal(organizationId: string, request: CreateDealRequest): Observable<CreateDealResult> {
    return this.http.post<CreateDealResult>(`${this.baseUrl(organizationId)}/deals`, request, { withCredentials: true });
  }

  updateDeal(organizationId: string, id: string, request: UpdateDealRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/deals/${id}`, request, { withCredentials: true });
  }

  moveDealToStage(organizationId: string, id: string, request: MoveDealToStageRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/deals/${id}/stage`, request, { withCredentials: true });
  }

  markDealWon(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl(organizationId)}/deals/${id}/mark-won`, {}, { withCredentials: true });
  }

  markDealLost(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl(organizationId)}/deals/${id}/mark-lost`, {}, { withCredentials: true });
  }

  // --- SMS module (Phase 18) ---

  listSmsTemplates(organizationId: string, page = 1, pageSize = 50): Observable<SmsTemplateListDto> {
    return this.http.get<SmsTemplateListDto>(`${this.baseUrl(organizationId)}/sms/templates`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  createSmsTemplate(organizationId: string, request: SmsTemplateRequest): Observable<SmsTemplateResult> {
    return this.http.post<SmsTemplateResult>(`${this.baseUrl(organizationId)}/sms/templates`, request, {
      withCredentials: true,
    });
  }

  updateSmsTemplate(organizationId: string, id: string, request: SmsTemplateRequest): Observable<SmsTemplateResult> {
    return this.http.put<SmsTemplateResult>(`${this.baseUrl(organizationId)}/sms/templates/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteSmsTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/sms/templates/${id}`, { withCredentials: true });
  }

  listSmsCreditLedger(organizationId: string, page = 1, pageSize = 50): Observable<SmsCreditLedgerDto> {
    return this.http.get<SmsCreditLedgerDto>(`${this.baseUrl(organizationId)}/sms/credit-ledger`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  adjustSmsCredit(organizationId: string, request: AdjustSmsCreditRequest): Observable<SmsCreditAdjustmentResult> {
    return this.http.post<SmsCreditAdjustmentResult>(`${this.baseUrl(organizationId)}/sms/credit-ledger/adjust`, request, {
      withCredentials: true,
    });
  }

  listSmsHistory(organizationId: string, page = 1, pageSize = 50): Observable<SmsLogListDto> {
    return this.http.get<SmsLogListDto>(`${this.baseUrl(organizationId)}/sms/history`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  sendSms(organizationId: string, request: SendSmsRequest): Observable<SendSmsResult> {
    return this.http.post<SendSmsResult>(`${this.baseUrl(organizationId)}/sms/send`, request, { withCredentials: true });
  }
}
